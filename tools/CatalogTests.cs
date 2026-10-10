using RandomLanguageMod;

var tables = new Dictionary<int, Dictionary<string, string>>
{
    [0] = new() { ["Play"] = "Play", ["Players"] = "Players: {0}/{1}", ["Missing"] = "Only English", ["Bad"] = "Bad {0}" },
    [11] = new() { ["Play"] = "プレイ", ["Players"] = "プレイヤー: {0}/{1}", ["Bad"] = "不正 {2}" },
    [8] = new() { ["Play"] = "Jouer", ["Players"] = "Joueurs : {0}/{1}" }
};
var catalog = new TranslationCatalog(tables, 0);
void Require(bool value, string message) { if (!value) throw new Exception(message); }
var play = catalog.Resolve("  PLAY\n")!;
Require(catalog.Render(play, 8) == "  JOUER\n", "Case and surrounding whitespace");
Require(catalog.Render(catalog.Resolve("プレイ")!, 0) == "Play", "Already translated labels");
Require(catalog.Render(catalog.Resolve("Players: 3/15")!, 11) == "プレイヤー: 3/15", "Formatted values preserved");
Require(catalog.Resolve("a user chat message") == null, "Unknown content unchanged");
var random = new Random(123);
int? previous = null;
var seen = new HashSet<int>();
for (int i = 0; i < 1000; i++)
{
    var choice = catalog.Choose(play, previous, random)!.Value;
    Require(previous != choice.Language, "Redisplay must change language");
    seen.Add(choice.Language);
    previous = choice.Language;
}
Require(seen.Count == 3, "All loaded languages selectable");
var restricted = new TranslationCatalog(tables, 0, new[] { 11 });
Require(restricted.Choose(catalog.Resolve("Play")!, 11, random)!.Value.Language == 11, "Single language fallback");
Require(restricted.Resolve("Players: 5/15") != null, "Source matching when source language excluded");
Require(catalog.Choose(catalog.Resolve("Only English")!, 0, random)!.Value.Language == 0, "Missing translations fallback");
Require(catalog.Render(catalog.Resolve("Bad 5")!, 11) == null, "Incompatible format rejected");
Require(tables[0]["Play"] == "Play", "Source tables preserved");
var itemTables = new Dictionary<int, Dictionary<string, string>>
{
    [0] = new() { ["Hat"] = "Top Hat", ["ColorRed"] = "Red", ["Pet"] = "Dog" },
    [8] = new() { ["Hat"] = "Top Hat", ["ColorRed"] = "Rouge", ["Pet"] = "Chien" },
    [11] = new() { ["Hat"] = "シルクハット", ["ColorRed"] = "赤", ["Pet"] = "犬" }
};
var items = new TranslationCatalog(itemTables, 0);
var hatName = items.Resolve("Top Hat")!;
for (int i = 0; i < 100; i++)
{
    var choice = items.Choose(hatName, 0, random, "Top Hat")!.Value;
    Require(choice.Language == 11 && choice.Text == "シルクハット", "Redisplay avoids an identical item-name translation");
}
Require(items.DistinctTranslations(hatName) == 2, "Distinct item-name translations counted");
var colorName = items.Resolve("Red", "ColorRed")!;
Require(items.Render(colorName, 8) == "Rouge", "Color names use their known translation key");
Require(items.Render(items.Resolve("Dog")!, 11) == "犬", "Pet names translated");
var onlySame = new TranslationCatalog(itemTables, 0, new[] { 0, 8 });
Require(onlySame.Choose(hatName, 0, random, "Top Hat")!.Value.Language == 8, "Language changes even when every item-name translation is identical");
Console.WriteLine("PASS: static and formatted labels, whitespace, uppercase, unknown content, 1000 rerolls, language filters, missing translations and source preservation.");
Console.WriteLine("PASS: cosmetic and color names, distinct displayed translations, identical-name fallback.");
