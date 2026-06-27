using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Quartermaster
{
    /// <summary>
    /// Data for a single floating label to render above a container.
    /// </summary>
    public class ContainerLabel
    {
        public double X;
        public double Y;
        public double Z;
        public string Text;
        public LoadedTexture Texture;
    }

    /// <summary>
    /// Renders floating text labels at container positions during the Ortho stage.
    /// Because it draws in 2D screen space (not in-world), labels are visible through walls.
    /// Registered for EnumRenderStage.Ortho so we can use PerspectiveViewMat/PerspectiveProjectionMat
    /// which are captured during the Opaque stage.
    /// </summary>
    public class ContainerLabelRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private List<ContainerLabel> labels = new List<ContainerLabel>();

        // Labels fade out between these distances (in blocks)
        private const double FadeStartDist = 20.0;
        private const double FadeEndDist = 40.0;
        private const double MaxRenderDist = 40.0;

        // Render after most Ortho elements but before GUI
        public double RenderOrder => 0.95;
        public int RenderRange => 999;

        public ContainerLabelRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>
        /// Called by QuartermasterModSystem when highlights are set.
        /// Creates text textures for each label.
        /// </summary>
        public void SetLabels(List<ContainerLabel> newLabels)
        {
            // Dispose old textures first
            ClearLabels();
            
            foreach (var label in newLabels)
            {
                // Generate a text texture using the GUI composer's text utility
                label.Texture = GenTextTexture(label.Text);
                labels.Add(label);
            }
        }

        /// <summary>
        /// Clears all labels and disposes their textures.
        /// </summary>
        public void ClearLabels()
        {
            foreach (var label in labels)
            {
                label.Texture?.Dispose();
            }
            labels.Clear();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (labels.Count == 0) return;

            IRenderAPI rapi = capi.Render;

            // Get matrices for projection
            double[] projMat = rapi.PerspectiveProjectionMat;
            // CameraMatrixOrigin is the view matrix with player at origin — matches our dx/dy/dz input
            double[] viewMat = rapi.CameraMatrixOrigin;

            // Player eye position
            var camPos = capi.World.Player.Entity.CameraPos;

            int fbWidth = rapi.FrameWidth;
            int fbHeight = rapi.FrameHeight;

            foreach (var label in labels)
            {
                if (label.Texture == null) continue;

                // World position of the label (center of block, offset above)
                double worldX = label.X + 0.5;
                double worldY = label.Y + 1.3;
                double worldZ = label.Z + 0.5;

                // Player-relative offset
                double dx = worldX - camPos.X;
                double dy = worldY - camPos.Y;
                double dz = worldZ - camPos.Z;
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist > MaxRenderDist) continue;

                // Apply view matrix (CameraMatrixOrigin, player at origin, column-major)
                double vx = viewMat[0] * dx + viewMat[4] * dy + viewMat[8]  * dz + viewMat[12];
                double vy = viewMat[1] * dx + viewMat[5] * dy + viewMat[9]  * dz + viewMat[13];
                double vz = viewMat[2] * dx + viewMat[6] * dy + viewMat[10] * dz + viewMat[14];
                double vw = viewMat[3] * dx + viewMat[7] * dy + viewMat[11] * dz + viewMat[15];

                // Apply projection matrix
                double px = projMat[0] * vx + projMat[4] * vy + projMat[8]  * vz + projMat[12] * vw;
                double py = projMat[1] * vx + projMat[5] * vy + projMat[9]  * vz + projMat[13] * vw;
                double pz = projMat[2] * vx + projMat[6] * vy + projMat[10] * vz + projMat[14] * vw;
                double pw = projMat[3] * vx + projMat[7] * vy + projMat[11] * vz + projMat[15] * vw;

                // Behind camera check
                if (pw <= 0) continue;

                // Perspective divide → normalized device coordinates [-1, 1]
                double ndcX = px / pw;
                double ndcY = py / pw;

                // Off-screen check (with some margin)
                if (ndcX < -1.5 || ndcX > 1.5 || ndcY < -1.5 || ndcY > 1.5) continue;

                // Viewport transform → screen pixels
                double screenX = (ndcX + 1.0) * 0.5 * fbWidth;
                double screenY = (1.0 - ndcY) * 0.5 * fbHeight;

                // Distance fade
                float alpha = 1.0f;
                if (dist > FadeStartDist)
                {
                    alpha = (float)(1.0 - (dist - FadeStartDist) / (FadeEndDist - FadeStartDist));
                    alpha = GameMath.Clamp(alpha, 0f, 1f);
                }
                if (alpha <= 0f) continue;

                // Center the texture on the projected point
                float texW = label.Texture.Width;
                float texH = label.Texture.Height;
                float drawX = (float)screenX - texW / 2f;
                float drawY = (float)screenY - texH / 2f;

                // Render the text texture
                rapi.Render2DTexturePremultipliedAlpha(
                    label.Texture.TextureId,
                    drawX, drawY,
                    texW, texH,
                    50f,
                    new Vec4f(1f, 1f, 1f, alpha)
                );
            }
        }

        /// <summary>
        /// Generates a text texture with a semi-transparent dark background.
        /// </summary>
        private LoadedTexture GenTextTexture(string text)
        {
            // Use CairoFont for styling — white text, reasonable size
            CairoFont font = CairoFont.WhiteSmallishText();
            font.WithColor(new double[] { 1, 1, 1, 1 });

            // Use the TextTextureUtil to generate a texture with background
            TextTextureUtil texUtil = capi.Gui.TextTexture;

            // Generate with a dark semi-transparent background for readability
            LoadedTexture tex = texUtil.GenTextTexture(
                text,
                font,
                new TextBackground()
                {
                    FillColor = GuiStyle.DialogStrongBgColor,
                    Padding = 5,
                    Radius = 3,
                    BorderWidth = 1,
                    BorderColor = GuiStyle.DialogBorderColor
                }
            );

            return tex;
        }

        public void Dispose()
        {
            ClearLabels();
        }
    }
}
