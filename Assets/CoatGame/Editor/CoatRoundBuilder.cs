using System.IO;
using UnityEditor;
using UnityEngine;
using Coat;

/// Creates the round stock asset, filled with placeholders.
///
/// The placeholders are deliberately nameless -- "Placeholder 1" and so on,
/// not "Parkour" -- because a list that reads like the real plan invites
/// someone to assume those rounds exist. They are pickable and do nothing.
/// Replacing one with a real round is: change the name, point Scene at
/// something, clear Placeholder.
public static class CoatRoundBuilder
{
    const string StockPath = "Assets/CoatGame/Resources/CoatRoundStock.asset";
    const int Placeholders = 6;

    [MenuItem("Coat/Build Round Stock")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        var existing = AssetDatabase.LoadAssetAtPath<CoatRoundStock>(StockPath);
        if (existing != null)
            return $"rounds: already there, {existing.Rounds.Count} entr(ies), left alone";

        Directory.CreateDirectory(Path.GetDirectoryName(StockPath));

        var stock = ScriptableObject.CreateInstance<CoatRoundStock>();
        for (int i = 1; i <= Placeholders; i++)
            stock.Rounds.Add(new RoundDef
            {
                Id = "placeholder-" + i,
                Name = "Placeholder " + i,
                Scene = "",
                Weight = 1f,
                Placeholder = true
            });

        AssetDatabase.CreateAsset(stock, StockPath);
        AssetDatabase.SaveAssets();
        CoatRounds.Forget();
        return $"rounds: created {StockPath} with {Placeholders} placeholders";
    }
}
