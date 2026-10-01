namespace GameAuthoringLab;

// Game policy for one acceptance fixture. These types are deliberately outside the runtime package.
internal sealed class TileMovementClock
{
    public const uint Left=1, Right=2, Jump=4, Pause=8, Restart=16, Exit=32;
    public const double StepSeconds=1d/60;
    private readonly InputActionMap _actions=new(
        InputBinding.Key(Left,PhysicalKey.A),InputBinding.Key(Left,PhysicalKey.Left),
        InputBinding.Key(Right,PhysicalKey.D),InputBinding.Key(Right,PhysicalKey.Right),
        InputBinding.Key(Jump,PhysicalKey.Space),InputBinding.Key(Pause,PhysicalKey.E),
        InputBinding.Key(Restart,PhysicalKey.T),InputBinding.Key(Exit,PhysicalKey.Escape));
    private double _accumulator;
    private bool _neutral=true,_jump;
    public bool Paused {get;private set;}
    public bool Suspended {get;private set;}
    public bool WaitingForNeutral=>_neutral;
    public bool RestartRequested {get;private set;}
    public bool Quit {get;private set;}
    public int Direction {get;private set;}
    public int Advance(InputSnapshot input,double seconds)
    {
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
        RestartRequested=false;Suspended=!input.Focused||!input.Drawable;
        if(Suspended)input.Flags&=~InputFlags.Focused; // Minimize has the same neutral gate as focus loss.
        var actions=_actions.Update(input);Quit=input.Quit!=0||(actions.Pressed&Exit)!=0;
        if(Suspended){ClearMotion();return 0;}
        if((actions.Pressed&Restart)!=0){RestartRequested=true;Paused=false;ClearMotion();return 0;}
        if((actions.Pressed&Pause)!=0){Paused=!Paused;ClearMotion();return 0;}
        if(Paused){ClearMotion();return 0;}
        if(_neutral)
        {
            // Raw keys prevent consumed/held controls from becoming fresh movement at a boundary.
            if(!Activity(input))_neutral=false;
            Direction=0;return 0;
        }
        Direction=((actions.Down&Right)!=0?1:0)-((actions.Down&Left)!=0?1:0);
        _jump|=(actions.Pressed&Jump)!=0;
        _accumulator+=Math.Min(seconds,.1); // Fixture catch-up cap; time beyond 100ms is deliberately discarded.
        int steps=(int)Math.Floor((_accumulator+1e-12)/StepSeconds);
        _accumulator-=steps*StepSeconds;if(_accumulator<0)_accumulator=0;
        return steps;
    }
    public bool ConsumeJump(){bool value=_jump;_jump=false;return value;}
    private void ClearMotion(){_neutral=true;_accumulator=0;_jump=false;Direction=0;}
    private static bool Activity(InputSnapshot input)
    {
        foreach(var key in Keys)if(input.KeyDown(key,true)||input.KeyPressed(key,true))return true;
        return false;
    }
    private static readonly PhysicalKey[] Keys=[PhysicalKey.A,PhysicalKey.Left,PhysicalKey.D,PhysicalKey.Right,PhysicalKey.Space,PhysicalKey.E,PhysicalKey.T,PhysicalKey.Escape];
}

internal sealed class TileMovementLevel:IDisposable
{
    internal const float HalfWidth=.35f,HalfHeight=.45f,Speed=4,SpawnX=6,SpawnY=10,GoalX=12,GoalY=11.45f;
    internal const ulong TerrainCategory=1,PlayerCategory=2,GoalCategory=4;
    private readonly PhysicsWorld _physics;
    private readonly PhysicsScope _scope;
    private readonly PhysicsShape _playerShape,_goalShape;
    private bool _disposed;
    public TileMapInstance Map {get;}
    public TileMapCollision Collision {get;}
    public PhysicsBody Player {get;}
    public bool InGoal {get;private set;}
    public int GoalEntries {get;private set;}
    public int GoalExits {get;private set;}
    public uint Steps {get;private set;}
    public PhysicsBodyState State {get{Check();return Player.State;}}
    public bool Grounded {get{Check();var s=Player.State;var hit=_physics.RayCast(s.X,s.Y,0,HalfHeight+.08f,PlayerCategory,TerrainCategory);return hit.Hit!=0&&hit.NormalY<-.5f&&s.Vy<.1f;}}
    public TileMovementLevel(EngineHost engine,PhysicsWorld physics,LoadedTileMap source)
    {
        _physics=physics;_scope=new(physics);Map=new(engine,source,new(64,64));
        try
        {
            Collision=Map.AttachCollision(physics,new(32),friction:0,category:TerrainCategory,mask:PlayerCategory);
            Player=_scope.CreateBody(new(PhysicsBodyType.Dynamic,SpawnX,SpawnY,FixedRotation:true));
            _playerShape=Player.AddShape(new(PhysicsShapeType.Box,HalfWidth,HalfHeight,Friction:0,Category:PlayerCategory,Mask:TerrainCategory|GoalCategory));
            var goal=_scope.CreateBody(new(PhysicsBodyType.Static,GoalX,GoalY));
            _goalShape=goal.AddShape(new(PhysicsShapeType.Box,.8f,.55f,Category:GoalCategory,Mask:PlayerCategory,Sensor:true));
        }
        catch{_scope.Dispose();Map.Dispose();throw;}
    }
    public void Step(int direction,bool jump)
    {
        Check();if(direction is <-1 or >1)throw new ArgumentOutOfRangeException(nameof(direction));
        var before=Player.State;
        Player.SetVelocity(direction*Speed,jump&&Grounded?-7:before.Vy);
        if(_physics.Step().Dropped!=0)throw new InvalidOperationException("Tile movement event queue overflow.");
        Steps++;
        foreach(var e in _physics.Events)
        {
            if(!((e.ShapeA==_goalShape.Id&&e.ShapeB==_playerShape.Id)||(e.ShapeB==_goalShape.Id&&e.ShapeA==_playerShape.Id)))continue;
            if(e.Type==3){InGoal=true;GoalEntries++;}
            else if(e.Type==4){InGoal=false;GoalExits++;}
        }
    }
    private void Check()=>ObjectDisposedException.ThrowIf(_disposed,this);
    public void Dispose(){if(_disposed)return;_scope.Dispose();Map.Dispose();_disposed=true;}
}
