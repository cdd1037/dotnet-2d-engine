using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
// Inspect metadata without loading the consumer or using reflection on runtime code.
foreach(string path in args){
 if(!File.Exists(path)){Console.WriteLine(JsonSerializer.Serialize(new{path,exists=false,types=Array.Empty<string>()}));continue;}
 using var stream=File.OpenRead(path);using var pe=new PEReader(stream);var reader=pe.GetMetadataReader();
 string[] types=reader.TypeDefinitions.Select(handle=>{var t=reader.GetTypeDefinition(handle);return reader.GetString(t.Namespace)+"."+reader.GetString(t.Name);}).Order(StringComparer.Ordinal).ToArray();
 Console.WriteLine(JsonSerializer.Serialize(new{path,exists=true,bytes=stream.Length,types}));
}
