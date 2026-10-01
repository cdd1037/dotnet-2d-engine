using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace GameAuthoringLab;
internal sealed class PhysicsFixtureDocument
{
    public required string Kind{get;init;}
    public required int Version{get;init;}
    public required float PixelsPerMeter{get;init;}
    public required float GravityX{get;init;}
    public required float GravityY{get;init;}
    public required float StepSeconds{get;init;}
    public required uint Substeps{get;init;}
    public required List<PhysicsFixtureBody> Bodies{get;init;}
    internal PhysicsSettings Settings=>new(GravityX,GravityY,StepSeconds,Substeps);
}
internal sealed class PhysicsFixtureBody
{
    public required string Name{get;init;}
    public required string Type{get;init;}
    public required string Shape{get;init;}
    public required float X{get;init;}
    public required float Y{get;init;}
    public required float A{get;init;}
    public required float B{get;init;}
    public float Angle{get;init;}
    public float Vx{get;init;}
    public float Vy{get;init;}
    public bool Sensor{get;init;}
    public float Restitution{get;init;}
    internal PhysicsBodyDefinition BodyDefinition=>new(Type switch{"static"=>PhysicsBodyType.Static,"kinematic"=>PhysicsBodyType.Kinematic,"dynamic"=>PhysicsBodyType.Dynamic,_=>throw new ArgumentOutOfRangeException(nameof(Type))},X,Y,Angle,Vx,Vy);
    internal PhysicsShapeDefinition ShapeDefinition=>new(Shape switch{"circle"=>PhysicsShapeType.Circle,"box"=>PhysicsShapeType.Box,_=>throw new ArgumentOutOfRangeException(nameof(Shape))},A,B,Restitution:Restitution,Sensor:Sensor);
}
internal sealed class PhysicsFixtureException(string file,string path,string message,Exception? inner=null):Exception($"{file} [PHYSICS_FIXTURE] {path}: {message}",inner);
internal static class PhysicsFixture
{
    public static PhysicsFixtureDocument LoadAsset(AssetRoot root,string path)
    {
        try
        {
            using var stream=File.OpenRead(root.Resolve(path));
            if(stream.Length is <1 or >65536)throw new PhysicsFixtureException(path,"$","Expected 1..65536 bytes.");
            byte[] bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
            if(stream.ReadByte()!=-1)throw new PhysicsFixtureException(path,"$","Source changed while reading.");
            return Load(new UTF8Encoding(false,true).GetString(bytes),path);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {throw new PhysicsFixtureException(path,"$",e.Message,e);}
    }
    public static PhysicsFixtureDocument Load(string json,string source)
    {
        if(json.Length>65536)throw new PhysicsFixtureException(source,"$","Document is too large.");
        PhysicsFixtureDocument doc;
        try{doc=JsonSerializer.Deserialize(json,PhysicsFixtureJsonContext.Default.PhysicsFixtureDocument)??throw new PhysicsFixtureException(source,"$","Null document.");}
        catch(JsonException e){throw new PhysicsFixtureException(source,e.Path??"$",e.Message,e);}
        if(doc.Kind!="gal-physics-fixture"||doc.Version!=1)throw new PhysicsFixtureException(source,"$.kind/version","Expected gal-physics-fixture version 1.");
        try{_ =new PhysicsScale(doc.PixelsPerMeter);doc.Settings.Validate();}catch(ArgumentOutOfRangeException e){throw new PhysicsFixtureException(source,"$",e.Message,e);}
        if(doc.Bodies is null||doc.Bodies.Count is <1 or >64)throw new PhysicsFixtureException(source,"$.bodies","Expected 1..64 bodies.");
        var names=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<doc.Bodies.Count;i++)
        {
            var body=doc.Bodies[i];if(body is null||string.IsNullOrWhiteSpace(body.Name)||body.Name.Length>64||!names.Add(body.Name))throw new PhysicsFixtureException(source,$"$.bodies[{i}].name","Expected unique nonempty name up to 64 characters.");
            try{body.BodyDefinition.Validate();body.ShapeDefinition.Validate();}catch(ArgumentOutOfRangeException e){throw new PhysicsFixtureException(source,$"$.bodies[{i}]",e.Message,e);}
        }
        return doc;
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,AllowDuplicateProperties=false,RespectNullableAnnotations=true,GenerationMode=JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PhysicsFixtureDocument))]
internal partial class PhysicsFixtureJsonContext:JsonSerializerContext;
