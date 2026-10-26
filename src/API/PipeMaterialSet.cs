using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes.API
{
    /// <summary>Library-owned materials configured during one type's resource initialization.</summary>
    /// <remarks>Texture and shader references remain owned by the extension. Configure these materials during the initialization callback; draw-time state belongs in the effect property block.</remarks>
    public sealed class PipeMaterialSet
    {
        private readonly Dictionary<PipeSurfaceRole, Material> materials =
            new Dictionary<PipeSurfaceRole, Material>();
        private readonly bool owned;
        private readonly PipeWallMode mode;
        private bool configuring;
        internal PipeWallMode WallMode => mode;

        /// <summary>The outer wall material.</summary>
        public Material Wall => materials[PipeSurfaceRole.Wall];

        /// <summary>The joint, retaining-ring and internal structural-member material.</summary>
        public Material Fittings => materials[PipeSurfaceRole.Fittings];

        /// <summary>The optional decorative conductor material.</summary>
        public Material Core => materials[PipeSurfaceRole.Core];

        /// <summary>The dark socket panel material.</summary>
        public Material MachinePanel => materials[PipeSurfaceRole.MachinePanel];

        /// <summary>The socket casing material.</summary>
        public Material MachineShell => materials[PipeSurfaceRole.MachineShell];

        internal PipeMaterialSet(PipeAppearance appearance)
        {
            owned = appearance != null;
            mode = appearance?.WallMode ?? PipeWallMode.Glass;
            materials[PipeSurfaceRole.Wall] =
                mode == PipeWallMode.Opaque
                    ? new Material(Shader.Find("Standard"))
                    {
                        color = new Color(0.8f, 0.83f, 0.85f),
                    }
                    : Copy(PipeVisuals.BodyMaterial);
            materials[PipeSurfaceRole.Fittings] = Copy(PipeVisuals.JointMaterial);
            materials[PipeSurfaceRole.MachinePanel] = Copy(PipeVisuals.MachinePanelMaterial);
            materials[PipeSurfaceRole.MachineShell] = Copy(PipeVisuals.MachineShellMaterial);
            if (owned)
            {
                materials[PipeSurfaceRole.Core] = new Material(Shader.Find("Standard"));
                configuring = true;
                try
                {
                    appearance.ConfigureMaterials?.Invoke(this);
                    ConfigureWall();
                    materials[PipeSurfaceRole.InnerWall] = new Material(Wall)
                    {
                        renderQueue = 2999,
                    };
                }
                catch
                {
                    Dispose();
                    throw;
                }
                finally
                {
                    configuring = false;
                }
            }
            else
            {
                materials[PipeSurfaceRole.InnerWall] = PipeVisuals.InnerWallMaterial;
                materials[PipeSurfaceRole.Core] = null;
            }
        }

        private Material Copy(Material material) => owned ? new Material(material) : material;

        /// <summary>Copies a supplied material into a configurable surface role.</summary>
        /// <param name="role">The target role; the interior wall is derived automatically.</param>
        /// <param name="source">A material whose textures and shader remain valid throughout rendering.</param>
        /// <exception cref="InvalidOperationException">The method is called outside the material configuration callback.</exception>
        /// <exception cref="ArgumentNullException">The source material is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The role is invalid or refers to the derived interior wall.</exception>
        public void SetMaterial(PipeSurfaceRole role, Material source)
        {
            if (!configuring)
            {
                throw new InvalidOperationException(
                    "Material replacement belongs in the configuration callback."
                );
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (role == PipeSurfaceRole.InnerWall || !materials.ContainsKey(role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            var replacement = new Material(source);
            UnityEngine.Object.Destroy(materials[role]);
            materials[role] = replacement;
        }

        private void ConfigureWall()
        {
            if (Wall.shader == null || !Wall.shader.isSupported)
            {
                throw new InvalidOperationException("The pipe wall shader is unavailable.");
            }

            bool glass = mode == PipeWallMode.Glass;
            Wall.SetOverrideTag("RenderType", glass ? "Transparent" : "Opaque");
            Wall.renderQueue = glass ? 3000 : 2000;
            if (Wall.HasProperty("_Mode"))
            {
                Wall.SetFloat("_Mode", glass ? 3 : 0);
                Wall.SetInt("_SrcBlend", (int)BlendMode.One);
                Wall.SetInt(
                    "_DstBlend",
                    (int)(glass ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero)
                );
                Wall.SetInt("_ZWrite", glass ? 0 : 1);
                Wall.DisableKeyword("_ALPHATEST_ON");
                Wall.DisableKeyword("_ALPHABLEND_ON");
                if (glass)
                {
                    Wall.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                }
                else
                {
                    Wall.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                }
            }
        }

        internal Material Get(PipeSurfaceRole role) => materials[role];

        internal void Dispose()
        {
            if (owned)
            {
                foreach (var material in materials.Values)
                {
                    if (material != null)
                    {
                        UnityEngine.Object.Destroy(material);
                    }
                }
            }

            materials.Clear();
        }
    }
}
