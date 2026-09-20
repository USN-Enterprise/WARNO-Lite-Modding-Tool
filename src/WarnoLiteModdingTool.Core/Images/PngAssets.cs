using System.Buffers.Binary;
using System.Text;
namespace WarnoLiteModdingTool.Core.Images;
public static class PngAssets
{
    public static byte[] Decode(string base64)
    {
        if(base64.Length>8_000_000)throw new InvalidDataException("图片数据过大");
        var bytes=Convert.FromBase64String(base64);
        if(bytes.Length<33||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||!bytes.AsSpan(12,4).SequenceEqual("IHDR"u8))throw new InvalidDataException("图片必须是PNG");
        var w=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));var h=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        if(w<=0||h<=0||w>4096||h>4096||(long)w*h>16_777_216)throw new InvalidDataException("图片尺寸超出4096像素");
        var offset=8;var hasData=false;var ended=false;
        while(offset+12<=bytes.Length){var length=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset,4));if(length<0||length>bytes.Length-offset-12)throw new InvalidDataException("PNG数据截断");var type=Encoding.ASCII.GetString(bytes,offset+4,4);if(type=="IDAT")hasData=true;offset+=12+length;if(type=="IEND"){ended=true;break;}}
        if(!ended||!hasData||offset!=bytes.Length)throw new InvalidDataException("PNG数据不完整");return bytes;
    }
}
