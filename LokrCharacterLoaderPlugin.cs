using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LokrModAPI;

namespace LokrCharacterLoader
{
	/// <summary>Plugin entry point that registers LokrCharacterLoader's default content sources and patches the base game.</summary>
	[BepInPlugin(Guid, Name, Version)]
	[BepInDependency(LokrModAPIPlugin.Guid)]
	public class LokrCharacterLoaderPlugin : BaseUnityPlugin
	{
		/// <summary>This plugin's BepInEx GUID.</summary>
		public const string Guid = "com.lokrmodding.characterloader";
		/// <summary>This plugin's display name.</summary>
		public const string Name = "LoKR Character Loader";
		/// <summary>This plugin's version string.</summary>
		public const string Version = "1.1.17";

		/// <summary>This plugin's shared BepInEx log source, set once in Awake().</summary>
		internal static ManualLogSource Log;

		private Harmony harmony;

		/// <summary>Registers default content sources and applies every Harmony patch.</summary>
		/// <remarks>Content sources are registered through the exact same CharacterAPI surface any other plugin would use -- see docs/modapi-plan.md §5.1 ("dogfooding"). Must happen before patches so the default sources are already wired up the moment any patched game method fires. Each patch class is applied on its own so one bad signature cannot abort the rest (1.1.10's PlaySound Type[] miss did exactly that).</remarks>
		private void Awake()
		{
			Log = base.Logger;

			DefaultContentSources.RegisterAll();

			harmony = new Harmony(Guid);
			PatchAllIsolated(harmony, typeof(LokrCharacterLoaderPlugin).Assembly);

			Log.LogInfo(string.Format(
				"{0} v{1} loaded — {2} method(s) patched.",
				Name, Version, harmony.GetPatchedMethods().Count()));
		}

		/// <summary>Applies each Harmony patch class on its own so one failure cannot abort the rest.</summary>
		private static void PatchAllIsolated(Harmony instance, Assembly assembly)
		{
			foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
			{
				try
				{
					instance.CreateClassProcessor(type).Patch();
				}
				catch (Exception ex)
				{
					Log.LogError("Harmony patch failed for " + type.FullName + ": " + ex);
				}
			}
		}
	}
}
