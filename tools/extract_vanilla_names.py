"""Read-only WARNO EDat/TRAD extraction; emits only the three display-name dictionaries.
Usage: python tools/extract_vanilla_names.py GAME_DATA_ROOT OUTPUT_JSON
No game or Mod files are modified; no third-party modules required.
"""
from pathlib import Path
import struct, json, sys, re

def entries(path):
    with path.open('rb') as f:
        header = f.read(80)
        version = struct.unpack_from('<I', header, 4)[0]
        if header[:4] != b'edat' or version not in (2, 3): raise ValueError('Unsupported EDat version: '+str(path))
        offset, size = struct.unpack_from('<II', header, 25 if version == 2 else 8)
        base = struct.unpack_from('<I', header, 33 if version == 2 else 16)[0]
        if offset + size > path.stat().st_size or size > 20000000: raise ValueError(path)
        f.seek(offset); data = f.read(size)
    if not data: return
    if data[:9] != b'\x09' + b'\0'*8: raise ValueError('Unsupported dictionary: '+str(path))
    def walk(start, end, prefix):
        while start < end:
            first, length = struct.unpack_from('<II', data, start)
            stop = start + length if length else end
            if not start < stop <= end: raise ValueError('Invalid EDat range')
            if first:
                name = data[start+8:data.index(b'\0', start+8, stop)].decode()
                yield from walk(start+first, stop, prefix+name)
            else:
                offset, size = struct.unpack_from('<QQ', data, start+8)
                name = data[start+40:data.index(b'\0', start+40, stop)].decode()
                yield prefix+name, base+offset, size, data[start+24:start+40], version
            start = stop
    yield from walk(9, len(data), '')

def trad(data):
    if data[:4] != b'TRAD': raise ValueError('Not a TRAD dictionary')
    count = struct.unpack_from('<I', data, 4)[0]
    if 8 + count*16 > len(data): raise ValueError('Invalid TRAD count')
    result = {}
    for i in range(count):
        key, offset, length = struct.unpack_from('<QII', data, 8+i*16)
        if offset < 8+count*16 or offset+length*2 > len(data): raise ValueError('Invalid TRAD string')
        text = data[offset:offset+length*2].decode('utf-16-le')
        if str(key) in result: raise ValueError('Duplicate TRAD key')
        result[str(key)] = text
    return result

if __name__ == '__main__':
    import hashlib, zlib
    root, output = map(Path, sys.argv[1:3]); result = {}; sources = []
    archives = sorted(root.glob('**/ZZ_1.dat'), key=lambda p: tuple(int(x) for x in p.parts if x.isdigit()))
    for archive in archives:
        for name, offset, size, digest, version in entries(archive):
            normalized = name.replace('\\', '/'); pieces = normalized.upper().split('/')
            language = next((x for x in ['US', 'SC'] if x in pieces), None)
            kind = pieces[-1].removesuffix('.DIC')
            if version == 3 and kind.endswith(('-US','-SC')): language, kind = kind[-2:], kind[:-3]
            if language is None or kind not in ['UNITS','COMPANIES','PLATOONS']: continue
            with archive.open('rb') as f: f.seek(offset); data = f.read(size)
            if (hashlib.md5(data).digest() != digest if version == 2 else zlib.crc32(data) != struct.unpack_from('<I', digest)[0]): raise ValueError('EDat checksum mismatch: '+name)
            rows = trad(data); result[language+'/'+kind] = rows
            sources.append({'archive':str(archive.relative_to(root)), 'dictionary':normalized, 'entries':len(rows)})
    if len(result) != 6: raise ValueError('Missing name dictionaries: '+str(result.keys()))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, separators=(',',':')), encoding='utf-8')
    output.with_suffix('.sources.json').write_text(json.dumps(sources,ensure_ascii=False,indent=2),encoding='utf-8')
    print({key:len(value) for key,value in result.items()})

