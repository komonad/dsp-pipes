using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes.API
{
    /// <summary>在单个类型的资源初始化期间配置的库自有材质。</summary>
    /// <remarks>纹理和着色器引用仍归扩展所有。在初始化回调期间配置这些材质；绘制时状态应放入效果属性块。</remarks>
    public sealed class PipeMaterialSet
    {
        private readonly Dictionary<PipeSurfaceRole, Material> materials =
            new Dictionary<PipeSurfaceRole, Material>();
        private readonly bool owned;
        private readonly PipeWallMode mode;
        private bool configuring;
        internal PipeWallMode WallMode => mode;

        /// <summary>外管壁材质。</summary>
        public Material Wall => materials[PipeSurfaceRole.Wall];

        /// <summary>接头、管箍和内部结构构件所用的材质。</summary>
        public Material Fittings => materials[PipeSurfaceRole.Fittings];

        /// <summary>可选的装饰性导体材质。</summary>
        public Material Core => materials[PipeSurfaceRole.Core];

        /// <summary>机器接口深色面板材质。</summary>
        public Material MachinePanel => materials[PipeSurfaceRole.MachinePanel];

        /// <summary>机器接口外壳材质。</summary>
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

        /// <summary>将提供的材质复制到可配置的表面角色。</summary>
        /// <param name="role">目标角色；内壁由系统自动派生。</param>
        /// <param name="source">其纹理和着色器在整个渲染期间保持有效的材质。</param>
        /// <exception cref="InvalidOperationException">在材质配置回调之外调用此方法。</exception>
        /// <exception cref="ArgumentNullException">源材质为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">角色无效，或指向派生的内壁。</exception>
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
