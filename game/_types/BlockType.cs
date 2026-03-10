namespace game
{
    public static class BlockType
    {
        public const byte Air = 0;
        public const byte Stone = 1;
        public const byte Dirt = 2;
        public const byte Grass = 3;
        public const byte Sand = 4;
        public const byte Water = 5;
        public const byte Wood = 6;
        public const byte Snow = 7;
        public const byte Leaves = 8;


        public static bool IsSolid(byte blockType)
        {
            return blockType != Air && blockType != Water;
        }


        public static bool IsTransparent(byte blockType)
        {
            return blockType == Air || blockType == Water;
        }


        public static Microsoft.Xna.Framework.Color GetBlockColor(byte blockType)
        {
            return blockType switch
            {
                Stone => new Microsoft.Xna.Framework.Color(128, 128, 128),
                Dirt => new Microsoft.Xna.Framework.Color(139, 90, 43),
                Grass => new Microsoft.Xna.Framework.Color(34, 139, 34),
                Leaves => new Microsoft.Xna.Framework.Color(24, 109, 44),
                Snow => new Microsoft.Xna.Framework.Color(255, 255, 255),
                Sand => new Microsoft.Xna.Framework.Color(238, 214, 175),
                Water => new Microsoft.Xna.Framework.Color(65, 105, 225),
                Wood => new Microsoft.Xna.Framework.Color(101, 67, 33),
                _ => new Microsoft.Xna.Framework.Color(255, 0, 255)
            };
        }
    }
}
