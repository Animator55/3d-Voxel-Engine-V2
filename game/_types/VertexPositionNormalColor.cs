using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
namespace game
{
    public struct VertexPositionNormalColor : IVertexType
    {
        public Vector3 Position { get; set; }
        public Vector3 Normal { get; set; }
        public Color Color { get; set; }

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
