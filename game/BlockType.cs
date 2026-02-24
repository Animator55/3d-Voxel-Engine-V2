namespace game
{
    /// <summary>
    /// Define los diferentes tipos de bloques disponibles en el mundo voxel.
    /// Se utiliza byte para optimizar memoria (256 tipos posibles por ahora).
    /// </summary>
    public static class BlockType
    {
        // Valores de tipo de bloque
        public const byte Air = 0;      // Vacío - no renderiza
        public const byte Stone = 1;    // Piedra base
        public const byte Dirt = 2;     // Tierra
        public const byte Grass = 3;    // Pasto
        public const byte Sand = 4;     // Arena
        public const byte Water = 5;    // Agua
        public const byte Wood = 6;     // Madera

        /// <summary>
        /// Verifica si un tipo de bloque es sólido (debe renderizarse y ocluye caras).
        /// </summary>
        public static bool IsSolid(byte blockType)
        {
            return blockType != Air && blockType != Water;
        }

        /// <summary>
        /// Verifica si un tipo de bloque es transparente/translúcido.
        /// </summary>
        public static bool IsTransparent(byte blockType)
        {
            return blockType == Air || blockType == Water;
        }

        /// <summary>
        /// Retorna un color representativo para el tipo de bloque.
        /// Se usa para debugging o si no hay texturas.
        /// </summary>
        public static Microsoft.Xna.Framework.Color GetBlockColor(byte blockType)
        {
            return blockType switch
            {
                Stone => new Microsoft.Xna.Framework.Color(128, 128, 128),  // Gris
                Dirt => new Microsoft.Xna.Framework.Color(139, 90, 43),     // Marrón
                Grass => new Microsoft.Xna.Framework.Color(34, 139, 34),    // Verde
                Sand => new Microsoft.Xna.Framework.Color(238, 214, 175),   // Beige
                Water => new Microsoft.Xna.Framework.Color(65, 105, 225),   // Azul
                Wood => new Microsoft.Xna.Framework.Color(101, 67, 33),     // Marrón oscuro
                _ => new Microsoft.Xna.Framework.Color(255, 0, 255)         // Magenta (desconocido)
            };
        }
    }
}
