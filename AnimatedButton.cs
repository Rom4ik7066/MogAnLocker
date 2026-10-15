using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MogLocker
{
    internal sealed class AnimatedButton : Button
    {
        private static bool animationsEnabled = true;

        public static bool AnimationsEnabled
        {
            get { return animationsEnabled; }
            set { animationsEnabled = value; }
        }

        private readonly Timer animationTimer;
        private Color startColor;
        private Color targetColor;
        private Color currentColor;
        private Color startBorderColor;
        private Color targetBorderColor;
        private Color currentBorderColor;
        private float progress = 1F;
        private int visualState;

        public AnimatedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            animationTimer = new Timer { Interval = 15 };
            animationTimer.Tick += AnimationTick;
            currentColor = BackColor;
            targetColor = BackColor;
            currentBorderColor = FlatAppearance.BorderColor;
            targetBorderColor = FlatAppearance.BorderColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color parentColor = GetOpaqueParentColor();
            e.Graphics.Clear(parentColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            if (!animationTimer.Enabled && visualState == 0)
                currentBorderColor = FlatAppearance.BorderColor;

            using (GraphicsPath path = CreateRoundedPath(bounds, Math.Min(12, bounds.Height / 2)))
            using (var background = new SolidBrush(Enabled
                ? currentColor
                : Blend(currentColor, parentColor, 0.55F)))
            using (var border = new Pen(Enabled
                ? currentBorderColor
                : Blend(currentBorderColor, parentColor, 0.55F)))
            {
                e.Graphics.FillPath(background, path);
                if (FlatAppearance.BorderSize > 0)
                {
                    border.Width = FlatAppearance.BorderSize;
                    e.Graphics.DrawPath(border, path);
                }
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
                Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = ClientRectangle;
                focusBounds.Inflate(-4, -4);
                using (GraphicsPath focusPath = CreateRoundedPath(focusBounds,
                    Math.Min(8, focusBounds.Height / 2)))
                using (var focusPen = new Pen(Color.FromArgb(175, 126, 224, 196)))
                {
                    focusPen.DashPattern = new[] { 2F, 2F };
                    e.Graphics.DrawPath(focusPen, focusPath);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetVisualState(1);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetVisualState(0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                SetVisualState(2);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            SetVisualState(ClientRectangle.Contains(e.Location) ? 1 : 0);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                SetVisualState(2);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                SetVisualState(1);
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if (animationTimer == null || !animationTimer.Enabled)
            {
                currentColor = BackColor;
                targetColor = BackColor;
                Invalidate();
            }
            else
            {
                SetVisualState(visualState);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Stop();
                animationTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void SetVisualState(int state)
        {
            visualState = state;
            Color nextColor = state == 2
                ? Blend(BackColor, Color.Black, 0.12F)
                : state == 1
                    ? Blend(BackColor, Color.White, 0.10F)
                    : BackColor;
            Color nextBorder = state == 0
                ? FlatAppearance.BorderColor
                : Blend(FlatAppearance.BorderColor, Color.FromArgb(126, 224, 196),
                    state == 2 ? 0.22F : 0.38F);

            if (!AnimationsEnabled)
            {
                animationTimer.Stop();
                currentColor = targetColor = nextColor;
                currentBorderColor = targetBorderColor = nextBorder;
                Invalidate();
                return;
            }

            if (nextColor == targetColor && nextBorder == targetBorderColor)
                return;

            startColor = currentColor;
            targetColor = nextColor;
            startBorderColor = currentBorderColor;
            targetBorderColor = nextBorder;
            progress = 0F;
            animationTimer.Start();
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            progress = Math.Min(1F, progress + 0.2F);
            float eased = progress * progress * (3F - 2F * progress);
            currentColor = Blend(startColor, targetColor, eased);
            currentBorderColor = Blend(startBorderColor, targetBorderColor, eased);
            Invalidate();
            if (progress >= 1F)
                animationTimer.Stop();
        }

        private static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                first.A + (int)((second.A - first.A) * amount),
                first.R + (int)((second.R - first.R) * amount),
                first.G + (int)((second.G - first.G) * amount),
                first.B + (int)((second.B - first.B) * amount));
        }

        private Color GetOpaqueParentColor()
        {
            Control ancestor = Parent;
            while (ancestor != null)
            {
                if (ancestor.BackColor.A == 255)
                    return ancestor.BackColor;
                ancestor = ancestor.Parent;
            }
            return SystemColors.Control;
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter,
                diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
