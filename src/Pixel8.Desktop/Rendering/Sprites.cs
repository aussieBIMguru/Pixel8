using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Pixel8.Desktop.Rendering;

/// <summary>
/// The 8×8 sprite set. Every sprite is 8 rows of 8 characters, one per pixel:
/// 0–9 and a–f are PICO-8 palette indices (see <see cref="Palette.ByIndex"/>), '.' is transparent.
/// They're baked into one texture at startup, so there's no content pipeline yet and the art lives
/// in code where it's easy to tweak. Swap for a real .png sheet (same 8×8 grid) when an artist joins.
/// </summary>
public sealed class SpriteSheet : IDisposable
{
    public const int Size = 8;

    private static readonly (string Name, string[] Rows)[] Defs =
    {
        ("hero", new[]
        {
            "..6666..",
            ".666666.",
            ".60ff06.",
            ".6ffff6.",
            "..cccc..",
            ".cc77cc.",
            "..c..c..",
            ".44..44.",
        }),
        ("chest", new[]
        {
            "........",
            ".444444.",
            "49999994",
            "44444444",
            "444aa444",
            "44499444",
            "44444444",
            "........",
        }),
        ("boss", new[]
        {
            ".777777.",
            "77777777",
            "78877887",
            "78877887",
            "77777777",
            ".777777.",
            "..7.7.7.",
            "..7.7.7.",
        }),
        ("fight", new[]
        {
            "6......6",
            ".6....6.",
            "..6..6..",
            "...66...",
            "...66...",
            "..4..4..",
            ".4....4.",
            "4......4",
        }),
        ("elite", new[]
        {
            "2......2",
            "22....22",
            ".288882.",
            ".8a88a8.",
            ".888888.",
            "..8778..",
            "..8888..",
            "........",
        }),
        ("trap", new[]
        {
            "........",
            "6..6..6.",
            "6..6..6.",
            "66.66.66",
            "66666666",
            "55555555",
            "........",
            "........",
        }),
        ("event", new[]
        {
            "..dddd..",
            ".dd..dd.",
            ".....dd.",
            "....dd..",
            "...dd...",
            "...dd...",
            "........",
            "...dd...",
        }),
        ("camp0", new[]
        {
            "....8...",
            "...88...",
            "..8998..",
            "..89a8..",
            ".89aa98.",
            "..4444..",
            ".44..44.",
            "........",
        }),
        ("camp1", new[]
        {
            "...8....",
            "...88...",
            "..8998..",
            "..8a98..",
            ".89aa98.",
            "..4444..",
            ".44..44.",
            "........",
        }),
        ("shop", new[]
        {
            "...44...",
            "..4..4..",
            "...44...",
            "..bbbb..",
            ".bbabbb.",
            ".bbaabb.",
            ".bbbbbb.",
            "..bbbb..",
        }),
        ("stairs", new[]
        {
            "........",
            "......cc",
            "......c.",
            "....ccc.",
            "....c...",
            "..ccc...",
            "..c.....",
            "ccc.....",
        }),
        ("unknown", new[]
        {
            "........",
            "........",
            "........",
            "...dd...",
            "...dd...",
            "........",
            "........",
            "........",
        }),
        ("glint", new[]
        {
            "........",
            "...a....",
            "...a....",
            ".aaaaa..",
            "...a....",
            "...a....",
            "........",
            "........",
        }),
        ("hero.fighter", new[]
        {
            "..6666..",
            ".666666.",
            ".60ff06.",
            ".6ffff6.",
            "..cccc..",
            ".cc77cc.",
            "..c..c..",
            ".44..44.",
        }),
        ("hero.rogue", new[]
        {
            "..dddd..",
            ".dddddd.",
            ".df0f0d.",
            "..dffd..",
            ".555556.",
            "..5555.6",
            "..5..5..",
            ".dd..dd.",
        }),
        ("hero.wizard", new[]
        {
            "...c....",
            "..ccc...",
            ".cccc...",
            "ccccccc.",
            ".f0f0...",
            ".7777.4.",
            ".cccc.4.",
            ".c..c.4.",
        }),
        ("hero.cleric", new[]
        {
            "..7777..",
            ".7ffff7.",
            ".7f0f07.",
            ".7ffff7.",
            "..7a77..",
            ".77a777.",
            ".777777.",
            "..7..7..",
        }),
        // Attack and cast frames (session 7): shown while a hero lunges or casts.
        ("hero.fighter.atk", new[]
        {
            "..6666..",
            ".666666.",
            ".60ff06.",
            ".6ffff6.",
            "..cccc76",
            ".cc77c..",
            "..c..c..",
            ".44..44.",
        }),
        ("hero.rogue.atk", new[]
        {
            "..dddd..",
            ".dddddd.",
            ".df0f0d.",
            "..dffd..",
            ".5555566",
            "..5555..",
            "..5..5..",
            ".dd..dd.",
        }),
        ("hero.wizard.atk", new[]
        {
            "...c....",
            "..ccc...",
            ".cccc...",
            "ccccccc.",
            ".f0f0...",
            ".777744.",
            ".cccc..4",
            ".c..c...",
        }),
        ("hero.wizard.cast", new[]
        {
            "...c..a.",
            "..ccc.4.",
            ".cccc.4.",
            "ccccccc.",
            ".f0f0.4.",
            ".7777.4.",
            ".cccc...",
            ".c..c...",
        }),
        ("hero.cleric.atk", new[]
        {
            "..7777..",
            ".7ffff7.",
            ".7f0f07.",
            ".7ffff7.",
            "..7a7744",
            ".77a777.",
            ".777777.",
            "..7..7..",
        }),
        ("hero.cleric.cast", new[]
        {
            "a.7777.a",
            ".7ffff7.",
            ".7f0f07.",
            ".7ffff7a",
            "..7777a.",
            ".777777.",
            ".777777.",
            "..7..7..",
        }),
        ("enemy.kobold", new[]
        {
            "........",
            "..999...",
            ".90999..",
            ".9999...",
            "..99.6..",
            ".9999.6.",
            "..99....",
            ".9..9...",
        }),
        ("enemy.giant_rat", new[]
        {
            "........",
            "........",
            "....555.",
            "..55555f",
            ".5555505",
            "5555555.",
            ".5.5.5..",
            "........",
        }),
        ("enemy.goblin", new[]
        {
            "........",
            "b.bbbb.b",
            ".b0bb0b.",
            ".bbbbbb.",
            "..b88b..",
            ".445544.",
            "..4..4..",
            ".33..33.",
        }),
        ("enemy.skeleton", new[]
        {
            "..777...",
            ".70707..",
            ".77777..",
            "..777...",
            ".7.7.7..",
            "7.777.7.",
            "..7.7...",
            ".77.77..",
        }),
        ("enemy.wolf", new[]
        {
            "........",
            "......66",
            ".....606",
            "6666666.",
            "666666..",
            "6.6..6..",
            "6.6..6..",
            "........",
        }),
        ("enemy.orc", new[]
        {
            "..3333..",
            ".333333.",
            ".303303.",
            ".333333.",
            ".373373.",
            "..4444..",
            ".445544.",
            ".33..33.",
        }),
        ("enemy.bugbear", new[]
        {
            "4......4",
            "44444444",
            "44044044",
            "44444444",
            ".488884.",
            "44444444",
            "44.44.44",
            "44....44",
        }),
        ("enemy.ogre", new[]
        {
            ".ffffff.",
            "ffffffff",
            "ff0ff0ff",
            "ffffffff",
            ".f8888f.",
            "44444444",
            "44.44.44",
            "ff....ff",
        }),
        ("i.attack", new[]
        {
            "......7.",
            ".....76.",
            "....76..",
            ".4.76...",
            "..46....",
            "..44....",
            ".4..4...",
            "........",
        }),
        ("i.guard", new[]
        {
            ".cccccc.",
            "c777777c",
            "c7cccc7c",
            "c7c77c7c",
            "c7cccc7c",
            ".c7777c.",
            "..c77c..",
            "...cc...",
        }),
        // Big attack (timed reaction): a lightning bolt
        ("i.bolt", new[]
        {
            "....aa..",
            "...aa...",
            "..aa....",
            ".aaaaa..",
            "...aa...",
            "..aa....",
            ".aa.....",
            ".a......",
        }),
        ("i.skull", new[]
        {
            "..7777..",
            ".777777.",
            ".707707.",
            ".777777.",
            "..7777..",
            "..7.7...",
            "........",
            "........",
        }),
        // Drawn tinted: white pixels take the tint colour, light grey ones come out a shade darker.
        ("floor", new[]
        {
            "77777777",
            "77777767",
            "77777777",
            "77677777",
            "77777777",
            "77777777",
            "76777777",
            "77777777",
        }),
    };

    private readonly Texture2D _texture;
    private readonly Dictionary<string, Rectangle> _frames = new();

    public SpriteSheet(GraphicsDevice device)
    {
        _texture = new Texture2D(device, Size * Defs.Length, Size);
        var data = new Color[Size * Defs.Length * Size];
        int stride = Size * Defs.Length;

        for (int i = 0; i < Defs.Length; i++)
        {
            var (name, rows) = Defs[i];
            if (rows.Length != Size || rows.Any(r => r.Length != Size))
                throw new InvalidOperationException($"Sprite '{name}' must be exactly {Size}x{Size}.");

            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    char ch = rows[y][x];
                    data[y * stride + i * Size + x] = ch == '.' ? Color.Transparent : Palette.ByIndex[Convert.ToInt32(ch.ToString(), 16)];
                }
            _frames[name] = new Rectangle(i * Size, 0, Size, Size);
        }
        _texture.SetData(data);
    }

    public bool Has(string name) => _frames.ContainsKey(name);

    public void Draw(SpriteBatch batch, string name, int x, int y, Color? tint = null, bool flipX = false) =>
        batch.Draw(_texture, new Vector2(x, y), _frames[name], tint ?? Color.White,
            0f, Vector2.Zero, 1f, flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);

    public void Dispose() => _texture.Dispose();
}
