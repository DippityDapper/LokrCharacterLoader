using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ironhide.Legends.Model.Metagame.Scripts;
using LokrModAPI;
using UnityEngine;
using File = System.IO.File;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Full-method replacement of IronhideScriptLoader.LoadScripts(string), driven by CharacterAPI.ResolvingScript instead of an inline Mods/ scan.</summary>
	/// <remarks>
	/// Row 15 in docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.5.
	///
	/// Incidental fix: the previous version's ad hoc "try mod path, then unconditionally re-check
	/// below" control flow logged a spurious "Duplicate script" warning every time a mod .lua
	/// override actually succeeded. Restructuring around a clean resolver call naturally removes
	/// that -- not a deliberate behavior change, just a side effect of not reproducing the old ad
	/// hoc double-check.
	/// </remarks>
	[HarmonyPatch(typeof(IronhideScriptLoader), "LoadScripts")]
	internal static class IronhideScriptLoaderPatches
	{
		/// <summary>Replaces IronhideScriptLoader.LoadScripts(path), resolving each script's source via CharacterAPI.ResolvingScript before loading it.</summary>
		[HarmonyPrefix]
		private static bool Prefix(IronhideScriptLoader __instance, string path)
		{
			Traverse traverse = Traverse.Create(__instance);
			Dictionary<string, TextAsset> loadedScripts = traverse.Field<Dictionary<string, TextAsset>>("loadedScripts").Value;
			List<string> loadedFolders = traverse.Field<List<string>>("loadedFolders").Value;
			Dictionary<string, string[]> scriptsInPath = traverse.Field<Dictionary<string, string[]>>("scriptsInPath").Value;

			if (loadedFolders.Any(s => path.StartsWith(s)))
			{
				Debug.LogWarningFormat("ScriptLoading: Ignoring path {0} because it was already loaded", new object[] { path });
				return false;
			}

			List<string> list = new List<string>();
			foreach (TextAsset textAsset in ResourcesWrapper.LoadAll<TextAsset>(path))
			{
				string name = textAsset.name;

				string overrideSource = CharacterAPI.RaiseResolvingScript(name);
				TextAsset assetToLoad = textAsset;
				if (overrideSource != null)
				{
					TextAsset overrideAsset = new TextAsset(overrideSource);
					overrideAsset.name = textAsset.name;
					assetToLoad = overrideAsset;
				}

				if (loadedScripts.ContainsKey(name))
				{
					Debug.LogWarningFormat("ScriptLoading: Duplicate script {0} in path: {1}", new object[] { name, path });
				}
				else
				{
					loadedScripts.Add(name, assetToLoad);
					list.Add(name);
				}
			}

			loadedFolders.Add(path);
			scriptsInPath.Add(path, list.ToArray());

			return false;
		}

		/// <summary>Registers the Mods/*/Lua file-convention script resolver.</summary>
		internal static void RegisterDefaults()
		{
			CharacterAPI.ResolvingScript += scriptName =>
			{
				if (ModAPI.Files.TryFindFile("Lua", scriptName + ".lua", out string path))
				{
					return File.ReadAllText(path);
				}
				return null;
			};
		}
	}
}
