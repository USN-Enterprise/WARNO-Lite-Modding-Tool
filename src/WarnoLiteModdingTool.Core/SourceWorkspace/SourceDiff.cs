namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public sealed record SourceDiffBlock(int BeforeLine, int BeforeCount, int AfterLine, int AfterCount);
public sealed record SourceDiffResult(IReadOnlyList<SourceDiffBlock> Blocks, bool Coarse);
public static class SourceDiff
{
    // Unique-line anchors keep large, mostly unchanged documents inexpensive.
    // Ambiguous large regions remain complete, explicitly coarse blocks.
    public static SourceDiffResult Compare(string before, string after, CancellationToken token = default)
    {
        var a = before.Replace("\r\n", "\n").Split('\n'); var b = after.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<SourceDiffBlock>(); var coarse = false;
        void Region(int x, int xe, int y, int ye, int depth)
        {
            token.ThrowIfCancellationRequested();
            while (x < xe && y < ye && a[x] == b[y]) { x++; y++; }
            while (x < xe && y < ye && a[xe - 1] == b[ye - 1]) { xe--; ye--; }
            if (x == xe && y == ye) return;
            if (x == xe || y == ye) { blocks.Add(new(x + 1, xe - x, y + 1, ye - y)); return; }
            if (depth > 40) { coarse = true; blocks.Add(new(x + 1, xe - x, y + 1, ye - y)); return; }
            var left = new Dictionary<string, int>(); var right = new Dictionary<string, int>();
            for (var i = x; i < xe; i++) { token.ThrowIfCancellationRequested(); if (!left.TryAdd(a[i], i)) left[a[i]] = -1; }
            for (var i = y; i < ye; i++) { token.ThrowIfCancellationRequested(); if (!right.TryAdd(b[i], i)) right[b[i]] = -1; }
            var candidates = left.Where(p => p.Value >= 0 && right.TryGetValue(p.Key, out var j) && j >= 0).Select(p => (X: p.Value, Y: right[p.Key])).OrderBy(p => p.X).ToArray();
            if (candidates.Length == 0)
            {
                if ((long)(xe - x) * (ye - y) <= 200_000)
                {
                    var lengths = new int[xe - x + 1, ye - y + 1];
                    for (var i = xe - x - 1; i >= 0; i--) { token.ThrowIfCancellationRequested(); for (var j = ye - y - 1; j >= 0; j--) lengths[i,j] = a[x+i] == b[y+j] ? lengths[i+1,j+1]+1 : Math.Max(lengths[i+1,j],lengths[i,j+1]); }
                    var anchors = new List<(int X,int Y)>(); var ai = 0; var bi = 0;
                    while (ai < xe-x && bi < ye-y) { if (a[x+ai] == b[y+bi]) { anchors.Add((x+ai++, y+bi++)); } else if (lengths[ai+1,bi] >= lengths[ai,bi+1]) ai++; else bi++; }
                    foreach (var anchor in anchors) { if (anchor.X > x || anchor.Y > y) blocks.Add(new(x+1,anchor.X-x,y+1,anchor.Y-y)); x=anchor.X+1;y=anchor.Y+1; }
                    if (x < xe || y < ye) blocks.Add(new(x+1,xe-x,y+1,ye-y));
                }
                else { coarse = true; blocks.Add(new(x+1,xe-x,y+1,ye-y)); }
                return;
            }
            var tails = new List<int>(); var previous = new int[candidates.Length];
            for (var i = 0; i < candidates.Length; i++)
            {
                var low=0;var high=tails.Count; while(low<high){var mid=(low+high)/2;if(candidates[tails[mid]].Y<candidates[i].Y)low=mid+1;else high=mid;}
                previous[i]=low==0?-1:tails[low-1]; if(low==tails.Count)tails.Add(i);else tails[low]=i;
            }
            var chain = new Stack<int>(); for(var i=tails[^1];i>=0;i=previous[i])chain.Push(i);
            foreach(var i in chain){var anchor=candidates[i];Region(x,anchor.X,y,anchor.Y,depth+1);x=anchor.X+1;y=anchor.Y+1;}
            Region(x,xe,y,ye,depth+1);
        }
        Region(0,a.Length,0,b.Length,0); return new(blocks,coarse);
    }
}
