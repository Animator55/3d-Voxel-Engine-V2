using Microsoft.Xna.Framework;

namespace game
{
    public static class BlockType
    {
        public const byte Air    = 0;
        public const byte Stone  = 1;
        public const byte Dirt   = 2;
        public const byte Grass  = 3;
        public const byte Sand   = 4;
        public const byte Water  = 5;
        public const byte Wood   = 6;
        public const byte Snow   = 7;
        public const byte Leaves = 8;
        public const byte Glowstone = 9;  

        public static bool IsSolid(byte blockType)
            => blockType != Air && blockType != Water;

        public static bool IsTransparent(byte blockType)
            => blockType == Air || blockType == Water;

        public static Color GetBlockColor(byte blockType)
        {
            return blockType switch
            {
                Stone     => new Color(128, 128, 128),
                Dirt      => new Color(139,  90,  43),
                Grass     => new Color( 34, 139,  34),
                Leaves    => new Color( 24, 109,  44),
                Snow      => new Color(255, 255, 255),
                Sand      => new Color(238, 214, 175),
                Water     => new Color( 65, 105, 225),
                Wood      => new Color(101,  67,  33),
                Glowstone => new Color(255, 220, 100), 
                _         => new Color(255,   0, 255)
            };
        }

        public static bool IsEmissive(byte blockType)
            => blockType == Glowstone;

        public static (Vector3 color, float radius, float intensity) GetEmissiveLight(byte blockType)
        {
            return blockType switch
            {
                Glowstone => (new Vector3(1.0f, 0.87f, 0.45f), radius: 28f, intensity: 2.0f),
                _         => (Vector3.Zero, 0f, 0f)
            };
        }
    }
}