using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Coffee.UIEffects;
using DarkTonic.MasterAudio;
using HarmonyLib;
using Ironhide.Legends.Model.Game.Units;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.Model.Metagame.Heroes;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Metagame.Screens.HeroRoom;
using LokrModAPI;
using UnityEngine;
using File = System.IO.File;
using Path = System.IO.Path;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Unifies three base-game sound call sites onto CharacterAPI.RegisterSoundResolver.</summary>
	/// <remarks>
	/// Rows 11, 12, 13 in docs/bepinex-migration-plan.md §6 / docs/modapi-plan.md §5.4. Replaces the
	/// old UnitPatches (sound half), UIHeroManagePatches, UIHeroRoomPatches -- all unified onto
	/// CharacterAPI.RegisterSoundResolver + ModAPI.Audio.PlayClip. Also loads a missing vanilla
	/// DynamicSoundGroup before MasterAudio.PlaySound so an Assassin reusing Asra/Cleaver FXMega
		/// still hears those clips. See docs/issues/resolved/fxmega-sounds-need-source-hero-group.md.
	/// </remarks>
	internal static class SoundPatches
	{
		/// <summary>Loads the vanilla DynamicSoundGroup that owns this clip when MasterAudio does not have it yet.</summary>
		/// <remarks>
		/// Combat SFX live on per-hero groups. Stage only instantiates the spawned unit's
		/// soundConfig.assetId, so ShadowStrikeCastFXMega is silent unless Asra's group is already
		/// in AudioController.loadedSoundGroups. TargetMethod looks the method up by name: 1.1.10
		/// baked the sixth parameter as string, but PlaySound takes double?, and HarmonyX aborted
		/// PatchAll for the whole plugin.
		/// </remarks>
		[HarmonyPatch]
		private static class MasterAudio_PlaySound_Patch
		{
			/// <summary>Resolves MasterAudio.PlaySound by name so nullable parameter types cannot miss.</summary>
			private static MethodBase TargetMethod()
			{
				return AccessTools.DeclaredMethod(typeof(MasterAudio), "PlaySound");
			}

			/// <summary>Skips this patch when PlaySound is missing instead of throwing from PatchAll.</summary>
			private static bool Prepare()
			{
				if (TargetMethod() != null)
				{
					return true;
				}

				LokrCharacterLoaderPlugin.Log.LogError(
					"VanillaSoundGroups: MasterAudio.PlaySound was not found; FXMega clip loading is disabled.");
				return false;
			}

			[HarmonyPrefix]
			private static void Prefix(ref string sType)
			{
				if (string.IsNullOrEmpty(sType))
				{
					return;
				}

				if (sType[0] == '#')
				{
					sType = sType.Substring(1);
				}

				VanillaSoundGroups.EnsureLoaded(sType);
			}
		}

		/// <summary>Resolves a unit's combat sound event via CharacterAPI, falling through to vanilla if none is found.</summary>
		[HarmonyPatch(typeof(Unit), "PlaySound")]
		private static class Unit_PlaySound_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(Unit __instance, string name, ref PlaySoundResult __result)
			{
				if (__instance.unitDefinition == null || __instance.unitDefinition.uniqueId == null)
				{
					return true;
				}
				AudioClip clip = CharacterAPI.ResolveSound(__instance.unitDefinition.uniqueId, name);
				if (clip == null)
				{
					return true;
				}
				ModAPI.Audio.PlayClip(clip);
				__result = new PlaySoundResult { SoundPlayed = true };
				return false;
			}
		}

		/// <summary>Full-method replacement of PromoteHero, resolving the "promote" sound via CharacterAPI within the skill-selected callback.</summary>
		/// <remarks>Still a full-method replacement (unchanged reason from the original migration): the sound check sits inside the skillSelectedAction delegate PromoteHero() creates, and Harmony can't target a compiler-generated lambda by a stable name.</remarks>
		[HarmonyPatch(typeof(UIHeroManage), "PromoteHero")]
		private static class UIHeroManage_PromoteHero_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(UIHeroManage __instance)
			{
				Traverse traverse = Traverse.Create(__instance);
				Hero hero = traverse.Field<Hero>("hero").Value;

				if (!__instance.CanLevelUpHero(hero))
				{
					return false;
				}

				__instance.promoteButton.GetComponent<UIShiny>().Stop(true);
				int num = Mathf.RoundToInt(hero.unitDefinition.stats["level"]);
				hero.unitDefinition.skillProgression[num + 1].ToList();
				int[] array = new int[] { -1, -1, 2, 3 };
				__instance.promoteButton.interactable = false;
				List<UIHeroManageSkillVariant.SkillConfig> skillConfigs = __instance.heroData.GetSkillConfigs(array[num + 1]);

				__instance.skillChooser.skillSelectedAction = delegate (UIHeroManageSkillVariant.SkillConfig config)
				{
					__instance.promoteButton.interactable = true;
					MetagameManager.instance.GuildManager.LevelUpHero(hero, config.skillPointer.AbilityId);
					MetagameManager.instance.SaveGameManager.Save();
					__instance.RefreshPanels(config.skillPointer.AbilityId);
					traverse.Method("CheckLevelUpAvailable").GetValue();
					__instance.skillChooser.skillSelectedAction = null;
					__instance.skillChooser.closedAction = null;
					__instance.skillChooser.Hide();

					AudioClip clip = CharacterAPI.ResolveSound(hero.unitDefinition.uniqueId, "promote");
					if (clip != null)
					{
						ModAPI.Audio.PlayClip(clip);
						__instance.heroData.PlayPromoteAnimation(Mathf.RoundToInt(hero.unitDefinition.stats["level"]));
						return;
					}

					SoundConfig soundConfig = hero.unitDefinition.soundConfig;
					string text = (soundConfig != null) ? soundConfig.soundClips.GetValueOrDefault("promote", null) : null;
					if (text != null)
					{
						MasterAudio.PlaySound(text, 1f, null, 0f, null, null, false, false);
					}
					__instance.heroData.PlayPromoteAnimation(Mathf.RoundToInt(hero.unitDefinition.stats["level"]));
				};
				__instance.skillChooser.closedAction = delegate ()
				{
					__instance.promoteButton.interactable = true;
					__instance.skillChooser.skillSelectedAction = null;
					__instance.skillChooser.closedAction = null;
				};
				__instance.skillChooser.Show(skillConfigs);

				return false;
			}
		}

		/// <summary>Resolves the "selectHero" sound via CharacterAPI, falling through to vanilla if none is found.</summary>
		[HarmonyPatch(typeof(UIHeroRoom), "PlayHeroSelectedSound")]
		private static class UIHeroRoom_PlayHeroSelectedSound_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(string heroId)
			{
				AudioClip clip = CharacterAPI.ResolveSound(heroId, "selectHero");
				if (clip == null)
				{
					return true;
				}
				ModAPI.Audio.PlayClip(clip);
				return false;
			}
		}

		/// <summary>Registers the Mods/*/Characters/&lt;unitId&gt;/sounds and Mods/*/Sounds file-convention resolver, with a per-path AudioClip cache.</summary>
		/// <remarks>The cache avoids re-reading/re-decoding the same WAV from disk on every repeatedly-triggered event (combat sounds especially). Checks a Lab character's own nested Characters/&lt;unitId&gt;/sounds/ folder first, falling back to the flat mod-wide Sounds/&lt;unitId&gt;/ convention unchanged -- that flat convention is what hand-authored, non-Lab mods already rely on, so it must keep working exactly as before; the nested lookup is purely additive.</remarks>
		internal static void RegisterDefaults()
		{
			Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

			CharacterAPI.RegisterSoundResolver((unitId, eventName) =>
			{
				List<string> candidates = ModAPI.Files
					.EnumerateCategoryFiles(Path.Combine("LokrCharacterLab", unitId, "sounds"))
					.Select(entry => entry.filePath)
					.Where(filePath => Path.GetFileName(filePath).Contains(eventName))
					.ToList();
				if (candidates.Count == 0)
				{
					candidates = ModAPI.Files
						.EnumerateCategoryFiles(Path.Combine("Characters", unitId, "sounds"))
						.Select(entry => entry.filePath)
						.Where(filePath => Path.GetFileName(filePath).Contains(eventName))
						.ToList();
				}
				if (candidates.Count == 0)
				{
					candidates = ModAPI.Files
						.EnumerateCategoryFiles(Path.Combine("Sounds", unitId))
						.Select(entry => entry.filePath)
						.Where(filePath => Path.GetFileName(filePath).Contains(eventName))
						.ToList();
				}
				if (candidates.Count == 0)
				{
					return null;
				}
				string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
				if (!cache.TryGetValue(chosen, out AudioClip clip))
				{
					clip = ModAPI.Assets.LoadAudioClip(chosen);
					cache[chosen] = clip;
				}
				return clip;
			});
		}

		/// <summary>Loads vanilla MasterAudio DynamicSoundGroup prefabs when a clip is requested but not registered.</summary>
		private static class VanillaSoundGroups
		{
			private const string GenericGroup = "DynamicSoundGroupGenericSkillSounds";
			private static readonly HashSet<string> FailedClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			private static readonly string[] AssetIds =
			{
				GenericGroup,
				"DynamicSoundGroupWulfSounds",
				"DynamicSoundGroupDemonSpawnSounds",
				"DynamicSoundGroupSasquatchSounds",
				"DynamicSoundGroupGolemSounds",
				"DynamicSoundGroupTeslaCoilSounds",
				"DynamicSoundGroupGeraldSounds",
				"DynamicSoundGroupAsraSounds",
				"DynamicSoundGroupOlochSounds",
				"DynamicSoundGroupBruxaSounds",
				"DynamicSoundGroupRegsonSounds",
				"DynamicSoundGroupBravebarkSounds",
				"DynamicSoundGroupKnightSounds",
				"DynamicSoundGroupRangerSounds",
				"DynamicSoundGroupBarbarianSounds",
				"DynamicSoundGroupArcaneMageSounds",
				"DynamicSoundGroupDarkKnightSounds",
				"DynamicSoundGroupBombardierSounds",
				"DynamicSoundGroupOrcCleaverSounds",
				"DynamicSoundGroupWitchDoctorSounds",
				"DynamicSoundGroupTeslaSounds",
				"DynamicSoundGroupSorceressSounds",
				"DynamicSoundGroupSylvanElfSounds",
				"DynamicSoundGroupMBFSounds",
				"DynamicSoundGroupDemonFlareonSounds",
				"DynamicSoundGroupSilveroakEnchantressSounds",
				"DynamicSoundGroupSkeletonSounds",
				"DynamicSoundGroupZombiesSounds",
				"DynamicSoundGroupMagmaElementalSounds",
				"DynamicSoundGroupSilveroakWebWeaverSounds",
				"DynamicSoundGroupWargSounds",
				"DynamicSoundGroupSpiderlingSounds",
				"DynamicSoundGroupAracnoGiantSpiderSounds",
				"DynamicSoundGroupTrollWarriorSounds",
				"DynamicSoundGroupTrollChampionSounds",
				"DynamicSoundGroupTrollChieftainSounds",
				"DynamicSoundGroupTrollBreakerSounds",
				"DynamicSoundGroupOrcHunterSounds",
				"DynamicSoundGroupExaltedLordSounds",
				"DynamicSoundGroupTogreSounds",
				"DynamicSoundGroupTrollzerkerSounds",
				"DynamicSoundGroupGoblinSounds",
				"DynamicSoundGroupOrcArcherSounds",
				"DynamicSoundGroupOrcShamanSounds",
				"DynamicSoundGroupOrcWarriorSounds",
				"DynamicSoundGroupOrcChampionSounds",
				"DynamicSoundGroupOgreSounds",
				"DynamicSoundGroupCultistAcolyteSounds",
				"DynamicSoundGroupCultistPriestSounds",
				"DynamicSoundGroupCultistAbominationSounds",
				"DynamicSoundGroupCultistExaltedSounds",
				"DynamicSoundGroupSilveroakWarlockSounds",
				"DynamicSoundGroupSilveroakBFSounds",
				"DynamicSoundGroupSilveroakSaplingSounds",
				"DynamicSoundGroupSilveroakTrunkSounds",
				"DynamicSoundGroupSilveroakNightshadeSounds",
				"DynamicSoundGroupSilveroakVinepawSounds",
				"DynamicSoundGroupWinterWulfSounds",
				"DynamicSoundGroupMatriarchSounds",
				"DynamicSoundGroupDemonLordSounds",
				"DynamicSoundGroupDemonHoundSounds",
			};

			/// <summary>Instantiates candidate sound-group prefabs until MasterAudio knows this clip, or records that it cannot.</summary>
			internal static void EnsureLoaded(string soundName)
			{
				if (string.IsNullOrEmpty(soundName)
					|| FailedClips.Contains(soundName)
					|| MasterAudio.SoundGroupExists(soundName))
				{
					return;
				}

				if (!MonoSingleton<AudioController>.IsInstanceValid)
				{
					return;
				}

				string token = OwnerToken(soundName);
				if (string.Equals(token, "Generic", StringComparison.OrdinalIgnoreCase))
				{
					TryLoad(GenericGroup);
				}
				else if (!string.IsNullOrEmpty(token))
				{
					for (int i = 0; i < AssetIds.Length; i++)
					{
						if (AssetIds[i].IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
						{
							TryLoad(AssetIds[i]);
							if (MasterAudio.SoundGroupExists(soundName))
							{
								LokrCharacterLoaderPlugin.Log.LogInfo(
									"VanillaSoundGroups: loaded '" + AssetIds[i] + "' for '" + soundName + "'.");
								return;
							}
						}
					}

					TryLoad(GenericGroup);
				}

				if (!MasterAudio.SoundGroupExists(soundName))
				{
					FailedClips.Add(soundName);
				}
			}

			/// <summary>Owner stem of a vanilla clip name (Asra from krl_sfx_combatAsra_shadowStrikeCharge).</summary>
			private static string OwnerToken(string soundName)
			{
				string rest = null;
				if (soundName.StartsWith("krl_sfx_combat", StringComparison.OrdinalIgnoreCase))
				{
					rest = soundName.Substring("krl_sfx_combat".Length);
				}
				else if (soundName.StartsWith("krl_va_combat", StringComparison.OrdinalIgnoreCase))
				{
					rest = soundName.Substring("krl_va_combat".Length);
				}

				if (string.IsNullOrEmpty(rest))
				{
					return null;
				}

				int split = rest.IndexOf('_');
				return split <= 0 ? rest : rest.Substring(0, split);
			}

			private static void TryLoad(string assetId)
			{
				try
				{
					MonoSingleton<AudioController>.Instance.LoadDynamicGroupAsset(assetId);
				}
				catch (Exception ex)
				{
					LokrCharacterLoaderPlugin.Log.LogWarning(
						"VanillaSoundGroups: could not load '" + assetId + "': " + ex.Message);
				}
			}
		}
	}
}
