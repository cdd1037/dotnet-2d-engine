namespace GameAuthoringLab;

/// <summary>Small reusable-audio sample; no callbacks, game rules, or implicit global music singleton.</summary>
internal static class AudioDemo
{
    public static int Run(int frames)
    {
        var root=new AssetRoot();var source=AuthoredScene.LoadAsset(root,"regions.scene.json");
        using var engine=new EngineHost(false,64,legacyTone:false);
        using var audio=engine.OpenAudio();using var scope=new AudioScope(audio);
        var clip=scope.LoadClip(root,"audio/cue.wav");var effect=scope.CreateVoice(clip);
        var music=scope.OpenStream(root,"audio/music.ogg");music.Play(-1);
        float volume=.5f;audio.SetGain(AudioGroup.Master,volume);
        using var textures=new TextureBank(engine,source.Catalog);textures.Sync(source.World);
        var batch=new SpriteBatch(16){RegionResolver=textures.ResolveRegion};
        var draw=new SpriteDrawV2[16];var camera=new Camera{Zoom=1};
        var actions=new InputActionMap(InputBinding.Key(1,PhysicalKey.E),InputBinding.Key(2,PhysicalKey.Space),
            InputBinding.Key(4,PhysicalKey.F),InputBinding.Key(8,PhysicalKey.T),InputBinding.Key(16,PhysicalKey.Left),
            InputBinding.Key(32,PhysicalKey.Right),InputBinding.Key(64,PhysicalKey.Escape));
        Console.WriteLine("AUDIO DEMO | E replay SFX | Space pause/resume music | F stop music | T restart loop | Left/Right master gain | Escape exit");
        Console.WriteLine("Bottom lamps: music green=playing, amber=paused, gray=stopped; blue=SFX playing; right row=master gain. Focus loss pauses both voices.");
        bool suspended=false,resumeMusic=false,resumeEffect=false;int rendered=0;
        while(frames==0||rendered<frames)
        {
            var input=engine.PollInput();var action=actions.Update(input);
            if(input.Quit!=0||(action.Pressed&64)!=0)break;
            bool active=input.Focused&&input.Drawable;
            if(!active&&!suspended)
            {resumeMusic=music.State.Playing&&!music.State.Paused;resumeEffect=effect.State.Playing&&!effect.State.Paused;if(resumeMusic)music.Pause();if(resumeEffect)effect.Pause();suspended=true;}
            if(active&&suspended)
            {if(resumeMusic)music.Resume();if(resumeEffect)effect.Resume();suspended=false;}
            if(active)
            {
                if((action.Pressed&1)!=0)effect.Play();
                if((action.Pressed&2)!=0){if(music.State.Paused)music.Resume();else if(music.State.Playing)music.Pause();else music.Play(-1);}
                if((action.Pressed&4)!=0)music.Stop();if((action.Pressed&8)!=0)music.Play(-1);
                if((action.Pressed&48)!=0){volume=Math.Clamp(volume+((action.Pressed&32)!=0?.1f:-.1f),0,1);audio.SetGain(AudioGroup.Master,volume);}
            }
            if(input.Drawable)
            {
                source.World.ExtractSprites(batch);batch.RegionDraws.CopyTo(draw);int count=batch.Count;
                var m=music.State;draw[count++]=m.Paused?Lamp(48,1,.65f,.15f):m.Playing?Lamp(48,.2f,.8f,.15f):Lamp(48,.2f,.2f,.2f);
                draw[count++]=Lamp(112,.15f,.3f,effect.State.Playing?1:.2f);
                draw[count++]=SpriteDrawV2.Create(new SpriteDraw{M11=1,M22=1,X=192,Y=420,Width=320*volume,Height=32,R=.3f,G=.7f,B=1,A=1});
                engine.Draw(camera,draw.AsSpan(0,count));rendered++;
            }
            Thread.Sleep(1);
        }
        scope.Dispose();var state=audio.State;
        if(state.Clips!=0||state.Voices!=0)throw new InvalidOperationException("Audio demo leaked a scene resource.");
        Console.WriteLine($"AUDIO DEMO DONE frames={rendered} clips=0 voices=0 backend={engine.Backend}; physical audibility not measured");return 0;
    }
    private static SpriteDrawV2 Lamp(float x,float r,float g,float b)=>SpriteDrawV2.Create(new SpriteDraw{M11=1,M22=1,X=x,Y=416,Width=40,Height=40,R=r,G=g,B=b,A=1});
}
