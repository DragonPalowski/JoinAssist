using System;
using System.Collections.Generic;
using System.Linq;

namespace JoinAssist
{
    public enum SearchTolerance { Precise, Balanced, Tolerant }
    public sealed class NameMatch
    {
        public int Score { get; internal set; }
        public int Band { get { return Score==int.MaxValue?int.MaxValue:Score/100000; } }
        public bool IsCorrection { get { return Band==6 || Band==7; } }
        public string Name { get; internal set; }
        public int[] Positions { get; internal set; } = new int[0];
    }
    public static class NameMatching
    {
        sealed class Profile {internal string Name,Compact,Core;internal int[] Map,Starts;}
        static readonly object sync=new object();
        static readonly Dictionary<string,Profile> profiles=new Dictionary<string,Profile>(StringComparer.Ordinal);
        static readonly Dictionary<string,NameMatch> matches=new Dictionary<string,NameMatch>(StringComparer.Ordinal);
        public static int Score(string name,string typed) {return Match(name,typed,PreferenceStore.Current.SearchTolerance).Score;}
        public static NameMatch Match(string name,string typed,SearchTolerance tolerance)
        {
            name=name??"";typed=typed??"";
            if(name.Length>256 || typed.Length>128)return Result(name,int.MaxValue);
            string key=(int)tolerance+"\0"+name+"\0"+typed;
            lock(sync){NameMatch cached;if(matches.TryGetValue(key,out cached))return cached;}
            var result=Find(GetProfile(name),typed,tolerance);
            lock(sync){if(matches.Count>=8192)matches.Clear();matches[key]=result;}return result;
        }
        static Profile GetProfile(string name)
        {
            lock(sync){Profile found;if(profiles.TryGetValue(name,out found))return found;}
            string core=name;
            foreach(string suffix in new[]{"_tb","_vw"})if(core.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)){core=core.Substring(0,core.Length-suffix.Length);break;}
            var map=Enumerable.Range(0,name.Length).Where(i=>char.IsLetterOrDigit(name[i])).ToArray();
            var starts=Enumerable.Range(0,map.Length).Where(i=>i==0 || map[i]!=map[i-1]+1 ||
                char.IsUpper(name[map[i]]) && (char.IsLower(name[map[i-1]]) || i+1<map.Length && char.IsLower(name[map[i+1]]))).ToArray();
            var value=new Profile{Name=name,Core=core,Compact=new string(map.Select(i=>name[i]).ToArray()),Map=map,Starts=starts};
            lock(sync){if(profiles.Count>=4096)profiles.Clear();profiles[name]=value;}return value;
        }
        static NameMatch Result(string name,int score,IEnumerable<int> positions=null)
        {return new NameMatch{Name=name,Score=score,Positions=positions==null?new int[0]:positions.ToArray()};}
        static NameMatch Find(Profile p,string typed,SearchTolerance tolerance)
        {
            if(typed.Length==0)return Result(p.Name,0);
            // Local-object markers are literal, in both directions, including ## versus #.
            string a=new string(p.Name.TakeWhile(c=>c=='#'||c=='@').ToArray()),b=new string(typed.TakeWhile(c=>c=='#'||c=='@').ToArray());
            if(a!=b)return Result(p.Name,int.MaxValue);
            if(string.Equals(p.Name,typed,StringComparison.OrdinalIgnoreCase))return Result(p.Name,0,Enumerable.Range(0,p.Name.Length));
            if(p.Name.StartsWith(typed,StringComparison.OrdinalIgnoreCase))return Result(p.Name,100000+Math.Max(0,p.Core.Length-typed.Length),Enumerable.Range(0,typed.Length));
            if(typed.Length<2)return Result(p.Name,int.MaxValue);
            int start=p.Name.IndexOf(typed,StringComparison.OrdinalIgnoreCase);
            if(start>=0)return Result(p.Name,(p.Starts.Any(i=>p.Map[i]==start)?200000:300000)+start*100+Math.Max(0,p.Core.Length-typed.Length),Enumerable.Range(start,typed.Length));
            string compact=new string(typed.Where(char.IsLetterOrDigit).ToArray());
            if(compact.Length<2)return Result(p.Name,int.MaxValue);
            start=p.Compact.IndexOf(compact,StringComparison.OrdinalIgnoreCase);
            if(start>=0)return Result(p.Name,400000+start*100+Math.Max(0,p.Compact.Length-compact.Length),p.Map.Skip(start).Take(compact.Length));
            if(compact.Length>=3)
            {
                var wordPositions=WordPrefixes(p,compact);
                if(wordPositions!=null)return Result(p.Name,500000+wordPositions.First()*100+Math.Max(0,p.Core.Length-compact.Length),wordPositions);
                if(tolerance!=SearchTolerance.Precise)
                {
                    NameMatch best=null;
                    for(int i=0;i<p.Compact.Length;i++)
                    {
                        if(!Equal(p.Compact[i],compact[0]))continue;
                        var positions=new List<int>{p.Map[i]};int j=i+1,k=1;
                        while(j<p.Compact.Length && k<compact.Length){if(Equal(p.Compact[j],compact[k])){positions.Add(p.Map[j]);k++;}j++;}
                        int gaps=j-i-compact.Length;
                        if(k==compact.Length && gaps<=Math.Max(4,compact.Length*2))
                        {var candidate=Result(p.Name,540000+gaps*100+i,positions);if(best==null || candidate.Score<best.Score)best=candidate;}
                    }
                    if(best!=null)return best;
                }
            }
            int budget=tolerance==SearchTolerance.Precise?0:tolerance==SearchTolerance.Balanced?(compact.Length>=9?2:compact.Length>=4?1:0):(compact.Length>=7?2:compact.Length>=3?1:0);
            if(budget==0)return Result(p.Name,int.MaxValue);
            int bestDistance=budget+1,bestStart=0,bestLength=0;
            foreach(int word in p.Starts)
            {
                if(compact.Length<5 && !Equal(compact[0],p.Compact[word]))continue;
                int length,distance=PrefixDistance(compact,p.Compact.Substring(word),budget,out length);
                if(distance>0 && (distance<bestDistance || distance==bestDistance && word<bestStart))
                {bestDistance=distance;bestStart=word;bestLength=length;}
            }
            if(bestDistance>budget)return Result(p.Name,int.MaxValue);
            var highlight=Align(compact,p.Compact.Substring(bestStart,bestLength)).Select(i=>p.Map[bestStart+i]);
            return Result(p.Name,500000+bestDistance*100000+bestStart*100+Math.Max(0,p.Core.Length-compact.Length),highlight);
        }
        static bool Equal(char a,char b) {return char.ToUpperInvariant(a)==char.ToUpperInvariant(b);}
        static int[] WordPrefixes(Profile p,string typed)
        {
            // One path per (word, typed offset), bounded by identifier length.
            var states=new Dictionary<int,List<int>>{{0,new List<int>()}};
            for(int w=0;w<p.Starts.Length;w++)
            {
                int start=p.Starts[w],end=w+1<p.Starts.Length?p.Starts[w+1]:p.Compact.Length;
                var next=new Dictionary<int,List<int>>(states);
                foreach(var state in states)
                {
                    int used=state.Key;
                    for(int n=0;start+n<end && used+n<typed.Length && Equal(p.Compact[start+n],typed[used+n]);n++)
                    {
                        var path=new List<int>(state.Value);path.AddRange(p.Map.Skip(start).Take(n+1));
                        int k=used+n+1;
                        if(path.Last()-path.First()+1-path.Count>Math.Max(6,typed.Length*2))continue;
                        if(k==typed.Length)return path.ToArray();if(!next.ContainsKey(k))next[k]=path;
                    }
                }
                states=next;
            }
            return null;
        }
        // Banded optimal-string-alignment distance, with a free untyped suffix.
        static int PrefixDistance(string typed,string name,int budget,out int length)
        {
            int n=typed.Length,m=Math.Min(name.Length,n+budget),inf=budget+1;length=0;
            if(m<n-budget)return inf;
            int[] previous=Enumerable.Repeat(inf,m+1).ToArray(),older=null;
            for(int j=0;j<=Math.Min(m,budget);j++)previous[j]=j;
            for(int i=1;i<=n;i++)
            {
                var row=Enumerable.Repeat(inf,m+1).ToArray();if(i<=budget)row[0]=i;
                for(int j=Math.Max(1,i-budget);j<=Math.Min(m,i+budget);j++)
                {
                    row[j]=Math.Min(previous[j]+1,Math.Min(row[j-1]+1,previous[j-1]+(Equal(typed[i-1],name[j-1])?0:1)));
                    if(i>1 && j>1 && Equal(typed[i-1],name[j-2]) && Equal(typed[i-2],name[j-1]))row[j]=Math.Min(row[j],older[j-2]+1);
                }
                older=previous;previous=row;
            }
            int best=inf;
            for(int j=Math.Max(1,n-budget);j<=m;j++)if(previous[j]<best || previous[j]==best && Math.Abs(n-j)<Math.Abs(n-length)){best=previous[j];length=j;}
            return best;
        }
        static IEnumerable<int> Align(string typed,string name)
        {
            int n=typed.Length,m=name.Length;var d=new int[n+1,m+1];
            for(int i=0;i<=n;i++)d[i,0]=i;for(int j=0;j<=m;j++)d[0,j]=j;
            for(int i=1;i<=n;i++)for(int j=1;j<=m;j++)
            {
                d[i,j]=Math.Min(d[i-1,j]+1,Math.Min(d[i,j-1]+1,d[i-1,j-1]+(Equal(typed[i-1],name[j-1])?0:1)));
                if(i>1 && j>1 && Equal(typed[i-1],name[j-2]) && Equal(typed[i-2],name[j-1]))d[i,j]=Math.Min(d[i,j],d[i-2,j-2]+1);
            }
            int x=n,y=m;var result=new List<int>();
            while(x>0 && y>0)
            {
                if(Equal(typed[x-1],name[y-1]) && d[x,y]==d[x-1,y-1]){result.Add(--y);x--;}
                else if(x>1 && y>1 && Equal(typed[x-1],name[y-2]) && Equal(typed[x-2],name[y-1]) && d[x,y]==d[x-2,y-2]+1){result.Add(y-1);result.Add(y-2);x-=2;y-=2;}
                else if(d[x,y]==d[x-1,y-1]+1){x--;y--;}
                else if(d[x,y]==d[x-1,y]+1)x--;else y--;
            }
            return result.OrderBy(i=>i);
        }
    }
}
