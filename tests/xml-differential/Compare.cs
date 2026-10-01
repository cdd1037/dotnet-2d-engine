using System.Text;
using System.Diagnostics;
using GameAuthoringLab;
internal static class Comparison
{
 static readonly UTF8Encoding Utf8=new(false,true);
 static string Model(string rml,string rcss,string name,int volume,string status,IReadOnlyList<string> items)=>string.Join("\0",new[]{rml,rcss,name,volume.ToString(),status}.Concat(items));
 static string Before(byte[] rml,byte[] rcss){try{var d=Original.UiAuthoring.Validate(rml,rcss,"fixture.rml","fixture.rcss");return "OK\0"+Model(d.Rml,d.Rcss,d.PlayerName,d.Volume,d.Status,d.Items);}catch(Original.UiAuthoringException e){return string.Join("\0","ERR",e.Code,e.FilePath,e.Line,e.Column,e.Field,e.Cause);}catch(Exception e){return "UNEXPECTED\0"+e.GetType().FullName+"\0"+e.Message;}}
 static string After(byte[] rml,byte[] rcss){try{var d=UiAuthoring.Validate(rml,rcss,"fixture.rml","fixture.rcss");return "OK\0"+Model(d.Rml,d.Rcss,d.PlayerName,d.Volume,d.Status,d.Items);}catch(UiAuthoringException e){return string.Join("\0","ERR",e.Code,e.FilePath,e.Line,e.Column,e.Field,e.Cause);}catch(Exception e){return "UNEXPECTED\0"+e.GetType().FullName+"\0"+e.Message;}}
 static int Main(string[] args){string root=args[0],rml=File.ReadAllText(Path.Combine(root,"settings.rml")),rcss=File.ReadAllText(Path.Combine(root,"settings.rcss"));int n=0,ok=0;var differences=new List<string>();
 void Check(string name,byte[] a,byte[] b){n++;string x=Before(a,b),y=After(a,b);if(x.StartsWith("OK"))ok++;if(x!=y){differences.Add(name+"\nBEFORE "+x.Replace('\0','|')+"\nAFTER "+y.Replace('\0','|'));File.WriteAllBytes(Path.Combine(args[1],$"diff-{n}.rml"),a);File.WriteAllBytes(Path.Combine(args[1],$"diff-{n}.rcss"),b);}}
 void Case(string name,string a,string? b=null)=>Check(name,Utf8.GetBytes(a),Utf8.GetBytes(b??rcss));
 Directory.CreateDirectory(args[1]);Case("valid",rml);
 var snippets=new[]{"A &amp; B","&#x1F600;","&#13;","&#10;","&#9;","&#0;","&#xD800;","&#x110000;","&missing;","A<!-- split -->B","{<!-- split -->{x}<!--split-->}","{{<!-- split -->x}}","&amp;<!-- split -->{{x}}","<![CDATA[x]]>","<?x y?>","<span>x</span>",""," ","A\r\nB","A\rB","A\tB"};
 foreach(string t in snippets)Case("text:"+t,rml.Replace("Ready / 就绪",t));
 foreach(string t in new[]{"xmlns=\"urn:test\"","xmlns=\"\"","xmlns:x=\"urn:test\"","xml:lang=\"en\"","unknown=\"x\"","id=\"x\"","xmlns:x=\"urn:test\" x:id=\"bad\""})Case("namespace/attr:"+t,rml.Replace("<body>","<body "+t+">"));
 foreach(string pre in new[]{"<!DOCTYPE rml>","<!DOCTYPE rml SYSTEM 'http://127.0.0.1:9/x'>","<!DOCTYPE rml SYSTEM 'file:///tmp/no-external-read'>","<!DOCTYPE rml [<!ENTITY x 'aaa'>]>","<!DOCTYPE rml [<!ENTITY % x SYSTEM 'http://127.0.0.1:9/x'>%x;]>"})Case("doctype",pre+rml[(rml.IndexOf("?>",StringComparison.Ordinal)+2)..]);
 Case("utf16decl",rml.Replace("utf-8","utf-16"));Case("version11",rml.Replace("1.0","1.1"));Case("bom","\uFEFF"+rml);Case("windows_newlines",rml.Replace("\n","\r\n"));Case("oldmac_newlines",rml.Replace("\n","\r"));
 for(int depth=0;depth<=20;depth++)Case("depth"+depth,rml.Replace("<body>","<body>"+string.Concat(Enumerable.Repeat("<div>",depth))).Replace("</body>",string.Concat(Enumerable.Repeat("</div>",depth))+"</body>"));
 foreach(int count in new[]{250,255,256,257,300})Case("count"+count,rml.Replace("<body>","<body>"+string.Concat(Enumerable.Repeat("<div />",count))));
 foreach(int count in new[]{100,1000,10000})Case("comment split"+count,rml.Replace("Ready / 就绪",string.Concat(Enumerable.Repeat("a<!--x-->",count))));
 foreach(int cp in new[]{0,1,8,9,10,11,12,13,14,31,32,127,128,0xD7ff,0xD800,0xDFFF,0xE000,0xFFFE,0xFFFF,0x10000,0x10ffff,0x110000})Case("codepoint"+cp,rml.Replace("Ready / 就绪","&#x"+cp.ToString("X")+";"));
 Case("long",rml+new string(' ',65537));Check("invalidutf8",new byte[]{0xc3,0x28},Utf8.GetBytes(rcss));
 var random=new Random(3127);string chars="<>/&;:=\"'!?{} \r\n\t012abc玩家";
 for(int i=0;i<6000;i++){string s=rml;int pos=random.Next(s.Length);s=(i%3) switch {0=>s.Remove(pos,1),1=>s.Insert(pos,chars[random.Next(chars.Length)].ToString()),_=>s[..pos]+chars[random.Next(chars.Length)]+s[(pos+1)..]};Case("mutation"+i,s);}
 for(int i=0;i<1000;i++){string s=rcss;int pos=random.Next(s.Length);s=(i%3) switch {0=>s.Remove(pos,1),1=>s.Insert(pos,chars[random.Next(chars.Length)].ToString()),_=>s[..pos]+chars[random.Next(chars.Length)]+s[(pos+1)..]};Case("style mutation"+i,rml,s);}
 File.WriteAllText(Path.Combine(args[1],"differences.txt"),string.Join("\n\n",differences));Console.WriteLine($"Differential cases={n} original_accept={ok} differences={differences.Count}");
 int checks=0;UiAuthoringTests.Run((pass,label)=>{checks++;if(!pass)throw new Exception(label);});Console.WriteLine($"Existing UI author tests PASS assertions={checks}");
 void Measure(string name,Action action){for(int i=0;i<100;i++)action();long start=GC.GetAllocatedBytesForCurrentThread(),time=Stopwatch.GetTimestamp();for(int i=0;i<1000;i++)action();Console.WriteLine($"{name}: mean_us={Stopwatch.GetElapsedTime(time).TotalMicroseconds/1000:F3} bytes_per_load={(GC.GetAllocatedBytesForCurrentThread()-start)/1000.0:F3}");}
 byte[] rb=Utf8.GetBytes(rml),cb=Utf8.GetBytes(rcss);Measure("original",()=>Original.UiAuthoring.Validate(rb,cb));Measure("candidate",()=>UiAuthoring.Validate(rb,cb));return differences.Count==0?0:1;
 }
}
