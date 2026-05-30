using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._KS14.WetOverlay;

public sealed class KsWetOverlay(ShaderInstance shader) : Overlay
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader = shader;

    private const int Length = 32;
    private readonly List<RainDroplet> _droplets = new(Length);
    private readonly Vector2[] _pos = new Vector2[Length];
    private readonly Vector2[] _data = new Vector2[Length];

    public void Init()
    {
        _droplets.Clear();

        for (var i = 0; i < Length; i++)
            _droplets.Add(CreateDroplet(randomY: true));
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is not { })
            return false;

        return true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var deltaTime = args.DeltaSeconds;
        for (var i = 0; i < _droplets.Count; i++)
        {
            var d = _droplets[i];

            // gravity / fall
            d.Position += d.Velocity * deltaTime;

            // slight acceleration
            d.Velocity.Y -= 0.05f * deltaTime;
            //d.Velocity *= float.Lerp(1f, 0.6f, d.Size / 0.05f);

            // reset when off-screen
            if (d.Position.Y < -0.1f)
                d = CreateDroplet(randomY: true);

            _droplets[i] = d;
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var maxCount = Math.Min(_droplets.Count, 32);

        for (var i = 0; i < maxCount; i++)
        {
            var d = _droplets[i];

            _pos[i] = d.Position;
            _data[i] = new(d.Size, 0f);
        }

        handle.UseShader(_shader);

        _shader.SetParameter("drop_count", maxCount);
        _shader.SetParameter("drops_pos", _pos);
        _shader.SetParameter("drops_data", _data);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture!);
        handle.DrawRect(args.WorldBounds, Color.White);

        handle.UseShader(null);
    }

    private RainDroplet CreateDroplet(bool randomY)
    {
        return new RainDroplet
        {
            Position = new Vector2(
                _robustRandom.NextFloat(),
                randomY ? _robustRandom.NextFloat() : 1f),

            Velocity = new Vector2(0f, -_robustRandom.NextFloat(0.02f, 0.15f)),

            Size = _robustRandom.NextFloat(0.1f, 0.7f),
            Streak = _robustRandom.NextFloat(0.5f, 2.5f)
        };
    }

    public struct RainDroplet
    {
        public Vector2 Position;   // 0–1 screen UV space
        public Vector2 Velocity;   // UV/sec
        public float Size;
        public float Streak;       // how much it stretches vertically
    }
}
