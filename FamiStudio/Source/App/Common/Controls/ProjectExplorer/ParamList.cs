using System;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices.Marshalling;

namespace FamiStudio
{
    public class ParamList : ParamControl
    {
        // MATTT : What was that again?
        private float bmpScale = Platform.IsMobile ? DpiScaling.Window * 0.25f : 1.0f;

        private TextureAtlasRef bmpLeft;
        private TextureAtlasRef bmpRight;

        private int buttonSizeX;
        private int buttonSizeY;
        private int hoverButtonIndex;
        private bool capture;
        private int captureButton;
        private double captureTime;

        public override bool SupportsDoubleClick => false;

        public ParamList(ParamInfo p) : base(p)
        {
            height = DpiScaling.ScaleForWindow(16);
        }

        protected override void OnAddedToContainer()
        {
            bmpLeft = ParentWindow.Graphics.GetTextureAtlasRef("ButtonLeft");
            bmpRight = ParentWindow.Graphics.GetTextureAtlasRef("ButtonRight");
            buttonSizeX = DpiScaling.ScaleCustom(bmpLeft.ElementSize.Width, bmpScale);
            buttonSizeY = DpiScaling.ScaleCustom(bmpLeft.ElementSize.Height, bmpScale);
            height = buttonSizeY;
        }

        // -1 = left, 1 = right, 0 = outside
        private int GetButtonIndex(int x) 
        {
            if (x < buttonSizeX)
                return -1;
            if (x > width - buttonSizeX)
                return 1;
            
            return 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Left && IsParamEnabled())
            {
                var buttonIndex = GetButtonIndex(e.X);
                if (buttonIndex != 0)
                {
                    Debug.Assert(!capture);
                    InvokeValueChangeStart();
                    captureTime = Platform.TimeSeconds();
                    capture = true;
                    captureButton = buttonIndex;
                    ChangeValue(buttonIndex);
                    SetTickEnabled(true);
                    Capture = true;
                    e.MarkHandled();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Left && capture)
            {
                capture = false;
                SetTickEnabled(false);
                InvokeValueChangeEnd();
                e.MarkHandled();
            }
            else if (e.Right && GetButtonIndex(e.X) == 0)
            {
                App.ShowContextMenu(new[]
                {
                    new ContextMenuOption("MenuReset", ResetDefaultValueContext, () => { ResetParamDefaultValue(); })
                });
                e.MarkHandled();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            SetAndMarkDirty(ref hoverButtonIndex, enabled ? GetButtonIndex(e.X) : 0);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            SetAndMarkDirty(ref hoverButtonIndex, 0);
        }

        private void ChangeValue(int delta)
        {
            var oldVal = param.GetValue();
            var newVal = param.SnapAndClampValue(oldVal + delta);
            param.SetValue(newVal);
            MarkDirty();
        }

        public override void Tick(float delta)
        {
            Debug.Assert(capture);

            if (capture)
            {
                var captureDuration = Platform.TimeSeconds() - captureTime;
                if (captureDuration > 0.35)
                    ChangeValue(captureButton);
            }
        }

        private bool IsParamEnabled()
        {
            return enabled && (param.IsEnabled == null || param.IsEnabled());
        }

        protected override void OnRender(Graphics g)
        {
            var c = g.DefaultCommandList;
            var paramEnabled = IsParamEnabled();
            var labelWidth = width - buttonSizeX * 2;
            var buttonOffsetY = Utils.DivideAndRoundUp(height - buttonSizeY, 2);
            var val = param.GetValue();
            var valPrev = param.SnapAndClampValue(val - 1);
            var valNext = param.SnapAndClampValue(val + 1);
            var valString = param.GetValueString();
            var opacity = paramEnabled ? 1.0f : 0.25f;
            var opacityL = paramEnabled && val != valPrev ? (hoverButtonIndex == -1 ? 0.6f : 1.0f) : 0.25f;
            var opacityR = paramEnabled && val != valNext ? (hoverButtonIndex ==  1 ? 0.6f : 1.0f) : 0.25f;

            c.DrawTextureAtlas(bmpLeft, 0, buttonOffsetY, bmpScale, Color.Black.Transparent(opacityL));

            if (valString.StartsWith("img:"))
            {
                var img = c.Graphics.GetTextureAtlasRef(valString.Substring(4));
                c.DrawTextureAtlasCentered(img, buttonSizeX, 0, labelWidth, height, 1, Color.Black);
            }
            else
            {
                c.DrawText(valString, Fonts.FontMedium, buttonSizeX, 0, Color.Black.Transparent(opacity), TextFlags.MiddleCenter, labelWidth, height);
            }

            c.DrawTextureAtlas(bmpRight, buttonSizeX + labelWidth, buttonOffsetY, bmpScale, Color.Black.Transparent(opacityR));
        }
    }
}
