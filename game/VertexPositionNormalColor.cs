using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace game
{
    /// <summary>
    /// Estructura personalizada de vértice que incluye posición, normal y color.
    /// MonoGame no proporciona esta estructura por defecto, así que la creamos.
    /// 
    /// Esta estructura es necesaria para:
    /// 1. Lighting/normales en BasicEffect
    /// 2. Color por vértice (para debug/coloring)
    /// 3. Greedy meshing que genera normales correctamente
    /// </summary>
    public struct VertexPositionNormalColor : IVertexType
    {
        public Vector3 Position { get; set; }
        public Vector3 Normal { get; set; }
        public Color Color { get; set; }

        // Descripción del layout en GPU (requerido por IVertexType)
        public static readonly VertexDeclaration VertexDeclaration;

        static VertexPositionNormalColor()
        {
            var elements = new VertexElement[]
            {
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0)
            };

            VertexDeclaration = new VertexDeclaration(elements);
        }

        public VertexPositionNormalColor(Vector3 position, Vector3 normal, Color color)
        {
            Position = position;
            Normal = normal;
            Color = color;
        }

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
