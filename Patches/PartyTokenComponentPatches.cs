using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ironhide.ExoSkeleton;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.Model.Metagame.Heroes;
using Ironhide.Legends.View.Map;
using UnityEngine;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Full-method replacement of PartyTokenComponent.UpdateHeroes(), stashing each hero's uniqueId for ExoSkeletonDataPatches to pick up, plus a SetFlagVisibility guard for missing banner parts.</summary>
	/// <remarks>
	/// Row 7 (party-token half) in docs/bepinex-migration-plan.md §6. Stashes each hero's uniqueId
	/// into ExoSkeletonModData.HeroId right before the existing ReplacePart(string, string) call
	/// (see ExoSkeletonDataPatches), since that call sits inside a plain for-loop with several
	/// private-field dependencies and no room in ReplacePart's own signature for the extra
	/// parameter the recompiled-DLL version added. FindPartIndex returns -1 on a miss; indexing
	/// parts[-1] throws, so both prefixes skip-and-log instead of inventing banner / unitOnMap meshes.
	/// </remarks>
	internal static class PartyTokenComponentPatches
	{
		/// <summary>Instance ids of party-token exos already warned for a missing Asst_Party_Banner.</summary>
		private static readonly HashSet<int> loggedMissingBanner = new HashSet<int>();

		/// <summary>Replaces PartyTokenComponent.UpdateHeroes(), rebuilding the map party-token icons and stashing each hero's uniqueId for ReplacePart to pick up.</summary>
		[HarmonyPatch(typeof(PartyTokenComponent), "UpdateHeroes")]
		private static class UpdateHeroes_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(PartyTokenComponent __instance)
			{
				List<Vector2[]> partVertices = Traverse.Create(__instance).Field<List<Vector2[]>>("partVertices").Value;

				__instance.unitsOnMap.ForEach(transform1 => Object.Destroy(transform1.gameObject));
				__instance.unitsOnMap.Clear();

				List<Hero> allHeroes = MetagameManager.instance.HeroManager.GetAllHeroes();
				__instance.mapUnitTemplate.SetActive(true);
				foreach (Hero hero in allHeroes)
				{
					GameObject gameObject = Object.Instantiate(__instance.mapUnitTemplate, __instance.mapUnitTemplate.transform.parent);
					__instance.unitsOnMap.Add(gameObject.transform);
				}
				__instance.mapUnitTemplate.SetActive(false);

				if (__instance.unitsOnMap.Count > 0)
				{
					Camera.main.GetComponent<NewMapCamera>().heroIcon = __instance.unitsOnMap[0];
				}

				for (int i = 0; i < __instance.unitsOnMap.Count; i++)
				{
					ExoSkeletonData componentInChildren = __instance.unitsOnMap[i].GetComponentInChildren<ExoSkeletonData>();
					string unitOnMap = allHeroes[i].unitDefinition.unitOnMap;
					if (!string.IsNullOrEmpty(unitOnMap))
					{
						ExoSkeletonModData.SetHeroId(componentInChildren, allHeroes[i].unitDefinition.uniqueId);
						if (!ContentRules.ShouldWritePartAtIndex(componentInChildren.FindPartIndex(unitOnMap))
							|| !ContentRules.ShouldWritePartAtIndex(componentInChildren.FindPartIndex("Asst_Party_Base")))
						{
							LokrCharacterLoaderPlugin.Log.LogWarning(string.Format(
								"PartyTokenComponent.UpdateHeroes: missing unitOnMap '{0}' or Asst_Party_Base on hero '{1}' — skip ReplacePart.",
								unitOnMap, allHeroes[i].unitDefinition.uniqueId));
						}
						else
						{
							componentInChildren.ReplacePart(unitOnMap, "Asst_Party_Base");
						}
					}
					int index = componentInChildren.FindPartIndex("Asst_Party_Banner");
					if (!ContentRules.ShouldWritePartAtIndex(index))
					{
						LokrCharacterLoaderPlugin.Log.LogWarning(string.Format(
							"PartyTokenComponent.UpdateHeroes: Asst_Party_Banner not found on hero '{0}' — skip vertex capture.",
							allHeroes[i].unitDefinition.uniqueId));
						partVertices.Add(System.Array.Empty<Vector2>());
					}
					else
					{
						Part part = componentInChildren.parts[index];
						partVertices.Add(part.vertices.ToArray());
					}
				}

				__instance.SetFlagVisibility(false);
				__instance.InterruptPartyMovement(true);

				return false;
			}
		}

		/// <summary>Skips a party token whose exo has no Asst_Party_Banner instead of indexing parts[-1].</summary>
		/// <remarks>UpdateHeroes calls SetFlagVisibility(false) after a possible banner skip; vanilla still walks every token and would NRE on the same miss.</remarks>
		[HarmonyPatch(typeof(PartyTokenComponent), "SetFlagVisibility")]
		private static class SetFlagVisibility_Patch
		{
			[HarmonyPrefix]
			private static bool Prefix(PartyTokenComponent __instance, bool isVisible)
			{
				List<Vector2[]> partVertices = Traverse.Create(__instance).Field<List<Vector2[]>>("partVertices").Value;

				for (int i = 0; i < __instance.unitsOnMap.Count; i++)
				{
					ExoSkeletonData componentInChildren = __instance.unitsOnMap[i].GetComponentInChildren<ExoSkeletonData>();
					int index = componentInChildren.FindPartIndex("Asst_Party_Banner");
					if (!ContentRules.ShouldWritePartAtIndex(index))
					{
						int instanceId = componentInChildren.GetInstanceID();
						if (loggedMissingBanner.Add(instanceId))
						{
							LokrCharacterLoaderPlugin.Log.LogWarning(
								"PartyTokenComponent.SetFlagVisibility: Asst_Party_Banner not found — skip unit.");
						}
						continue;
					}

					Part part = componentInChildren.parts[index];
					if (i == 0)
					{
						if (isVisible)
						{
							for (int j = part.vertices.Length - 1; j >= 0; j--)
							{
								part.vertices[j] = partVertices[i][j];
							}
						}
						else
						{
							for (int k = part.vertices.Length - 1; k >= 0; k--)
							{
								part.vertices[k] = Vector2.zero;
							}
						}
					}
					else
					{
						for (int l = part.vertices.Length - 1; l >= 0; l--)
						{
							part.vertices[l] = Vector2.zero;
						}
					}
				}

				return false;
			}
		}
	}
}
