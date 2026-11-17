using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.CombatMode
{
    public sealed partial class ColoredScreenBorderOverlay : Overlay
    {
        private static readonly ProtoId<ShaderPrototype> Shader = "ColoredScreenBorder";

        [Dependency] private IPrototypeManager _prototypeManager = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;
        private readonly ShaderInstance _shader;

        public ColoredScreenBorderOverlay()
        {
            IoCManager.InjectDependencies(this);
            _shader = _prototypeManager.Index(Shader).Instance();
        }

        // KS14 start: not drawn into portal views (viewer state or screen-texture effect)
        protected override bool BeforeDraw(in OverlayDrawArgs args)
        {
            return !Content.Client._KS14.Portal.KsPortalRenderSystem.PassActive;
        }
        // KS14 end

        protected override void Draw(in OverlayDrawArgs args)
        {
            var worldHandle = args.WorldHandle;
            worldHandle.UseShader(_shader);
            var viewport = args.WorldAABB;
            worldHandle.DrawRect(viewport, Color.White);
            worldHandle.UseShader(null);
        }
    }
}
