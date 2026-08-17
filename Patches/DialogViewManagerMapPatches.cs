using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DarkTonic.MasterAudio;
using DG.Tweening;
using HarmonyLib;
using Ironhide.AssetBundles;
using Ironhide.ExoSkeleton;
using Ironhide.Legends;
using Ironhide.Legends.Model.Metagame;
using Ironhide.Legends.Model.Metagame.Dialogs;
using Ironhide.Legends.Model.Metagame.Dialogs.Challenges;
using Ironhide.Legends.Model.Metagame.Heroes;
using Ironhide.Legends.Model.Metagame.Inventory;
using Ironhide.Legends.Utils;
using Ironhide.Legends.View.Game.Dialogs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LokrCharacterLoader.Patches
{
	/// <summary>Full-method replacement of DialogViewManagerMap.RefreshText(), resolving CHALLENGE portraits via CharacterAPI.</summary>
	/// <remarks>
	/// Row 10 (DialogViewManagerMap half) in docs/bepinex-migration-plan.md §6 / §5.3 of
	/// docs/modapi-plan.md -- see the original migration plan for why this can't be a smaller
	/// Prefix/Postfix. Both CHALLENGE portrait insertion points go through
	/// CharacterAPI.ResolvePortrait + PortraitPatches' shared ReplaceWithFlatImage helper instead
	/// of scanning Mods/ inline.
	/// </remarks>
	[HarmonyPatch(typeof(DialogViewManagerMap), "RefreshText")]
	internal static class DialogViewManagerMapPatches
	{
		private static readonly MethodInfo ParseTextWithMultiLinesMethod =
			AccessTools.Method(typeof(DialogViewManager), "ParseTextWithMultiLines");
		private static readonly MethodInfo ClearResponsesMethod =
			AccessTools.Method(typeof(DialogViewManager), "ClearResponses");
		private static readonly MethodInfo HandleContinueMethod =
			AccessTools.Method(typeof(DialogViewManager), "HandleContinue");
		private static readonly MethodInfo HandleContinueMultiLinesMethod =
			AccessTools.Method(typeof(DialogViewManager), "HandleContinueMultiLines");
		private static readonly MethodInfo ClearBallonsMethod =
			AccessTools.Method(typeof(DialogViewManagerMap), "ClearBallons");

		/// <summary>Replaces DialogViewManagerMap.RefreshText(), rendering dialog text/responses and resolving CHALLENGE portraits via CharacterAPI.</summary>
		[HarmonyPrefix]
		private static bool Prefix(DialogViewManagerMap __instance)
		{
			Traverse traverse = Traverse.Create(__instance);

			List<string[]> linesPerPages = traverse.Field<List<string[]>>("linesPerPages").Value;
			bool mustClearBallonsStack = traverse.Field<bool>("mustClearBallonsStack").Value;
			int pageIndex = traverse.Field<int>("pageIndex").Value;
			int lineIndex = traverse.Field<int>("lineIndex").Value;
			TweenTextConsoleSimulator kRLTextConsoleSimulator = traverse.Field<TweenTextConsoleSimulator>("kRLTextConsoleSimulator").Value;
			DialogViewResponseButton clickToContinue = traverse.Field<DialogViewResponseButton>("clickToContinue").Value;
			DialogViewResponseButton clickToEndField = traverse.Field<DialogViewResponseButton>("clickToEnd").Value;
			DialogViewResponseButton responseButtonPrefab = traverse.Field<DialogViewResponseButton>("responseButton").Value;

			Action<int> handleContinue = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), __instance, HandleContinueMethod);
			Action<int> handleContinueMultiLines = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), __instance, HandleContinueMultiLinesMethod);

			ClearResponsesMethod.Invoke(__instance, null);

			Dialog currentDialog = MetagameManager.instance.DialogManager.CurrentDialog;
			DialogDisplayInfo info = currentDialog.info;
			if (linesPerPages.Count == 0)
			{
				ParseTextWithMultiLinesMethod.Invoke(__instance, new object[] { currentDialog });
				linesPerPages = traverse.Field<List<string[]>>("linesPerPages").Value;
			}
			if (mustClearBallonsStack)
			{
				ClearBallonsMethod.Invoke(__instance, null);
			}

			string text = info.mainText;
			string[] array = null;
			if (text != null && (text.Contains("##") || text.Contains("||")))
			{
				array = linesPerPages[pageIndex];
				text = linesPerPages[pageIndex][lineIndex];
			}
			if (text != null)
			{
				string text2 = text;
				if (info.icon != null)
				{
					Sprite sprite = AssetBundleManager.LoadAsset<Sprite>(AssetBundleIds.IMAGES, info.icon);
					if (sprite == null)
					{
						sprite = __instance.defaultEventIcon;
					}
					__instance.mapConfig.icon.sprite = sprite;
				}
				if (info.narrator != null)
				{
					LocalizationComponent.SetFinalText(__instance.mapConfig.eventTitle, info.narrator, false);
				}
				GameObject gameObject = UnityEngine.Object.Instantiate(__instance.descriptionBallon, __instance.mapConfig.eventDescription.transform.parent);
				gameObject.transform.ResetLocal();
				__instance.mapConfig.ballons.Add(gameObject);
				gameObject.SetActive(true);
				LocalizationComponent.SetFinalText(gameObject.GetComponent<TextMeshProUGUI>(), text2, false);
				__instance.mapConfig.eventDescription.gameObject.SetActive(false);
				kRLTextConsoleSimulator = gameObject.GetComponent<TweenTextConsoleSimulator>();
				traverse.Field<TweenTextConsoleSimulator>("kRLTextConsoleSimulator").Value = kRLTextConsoleSimulator;
				kRLTextConsoleSimulator.Init();
				__instance.HideChallengeCheat();
			}

			if ((info.responses.Count > 0 && array != null && lineIndex == array.Length - 1 && pageIndex == linesPerPages.Count - 1) || (info.responses.Count > 0 && array == null))
			{
				traverse.Field<bool>("mustClearBallonsStack").Value = true;
				for (int i = 0; i < info.responses.Count; i++)
				{
					DialogDisplayInfoResponse responseInfo = info.responses[i];
					DialogViewResponseButton component = UnityEngine.Object.Instantiate(responseButtonPrefab, __instance.responseContainer).GetComponent<DialogViewResponseButton>();
					if (i % 2 == 0)
					{
						component.GetComponent<Image>().sprite = __instance.responseButtonSpritePair;
					}
					RectTransform rectTransform = component.transform.GetComponent<RectTransform>();
					int num = -5;
					rectTransform.pivot = new Vector2(0.5f, num);
					rectTransform.GetComponent<Image>().color = (responseInfo.enabled ? Color.white : Color.gray);
					Selectable component2 = rectTransform.GetComponent<Selectable>();
					if (component2)
					{
						component2.interactable = responseInfo.enabled;
					}
					rectTransform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
					rectTransform.DOScale(Vector3.one, 0.25f).SetDelay(0.2f * i);
					DOTween.To(() => rectTransform.pivot, x => { rectTransform.pivot = x; }, new Vector2(0.5f, 0.5f), 0.5f)
						.SetEase(Ease.OutSine).SetDelay(0.15f * i)
						.OnStart(() => MasterAudio.PlaySound("krl_sfx_mapGeneric_openBookAnswer", 1f, null, 0f, null, null, false, false));
					component.gameObject.SetActive(true);
					component.index = i;
					if (responseInfo.enabled)
					{
						component.callback = new Action<int>(__instance.HandleResponse);
					}
					else
					{
						component.callback = new Action<int>(__instance.HandleNotEnabledResponse);
					}
					string text3 = responseInfo.text;
					Hero hero3 = MetagameManager.instance.HeroManager.GetAllHeroes().FirstOrDefault(hero => hero.heroDefinition.guid == responseInfo.whoIdOriginal);
					if (hero3 != null)
					{
						component.whoPortraitExo.SetActive(true);
						ExoSkeletonData componentInChildren = component.whoPortraitExo.GetComponentInChildren<ExoSkeletonData>();
						componentInChildren.UpdateAsset(hero3.exoSkeletonDataAsset);
						componentInChildren.SetAnimFrame("Portrait", "StandStatic", 0);

						Sprite challengeSprite = CharacterAPI.ResolvePortrait(hero3.unitDefinition.uniqueId, "CHALLENGE");
						if (challengeSprite != null)
						{
							PortraitPatches.ReplaceWithFlatImage(componentInChildren.gameObject, challengeSprite, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
						}
					}
					component.SetText(text3);
					if (responseInfo.challenge != null)
					{
						if (responseInfo.challenge is DiceChallenge)
						{
							DiceChallenge diceChallenge = (DiceChallenge)responseInfo.challenge;
							component.transform.FindDeepChild("DiceChallengePreview").gameObject.SetActive(true);
							Transform transform = component.transform.FindDeepChild("Dice");
							Transform parent = transform.parent;
							int count = diceChallenge.diceResults.Count;
							if (count >= 4)
							{
								parent.GetComponent<HorizontalLayoutGroup>().spacing = Mathf.RoundToInt(-7.528379f + 12.89596f * count - 4.875114f * Mathf.Pow(count, 2f) + 0.3459818f * Mathf.Pow(count, 3f));
							}
							foreach (DiceChallenge.DiceResult diceResult in diceChallenge.diceResults)
							{
								Hero hero2 = diceResult.hero;
								Transform transform2 = UnityEngine.Object.Instantiate(transform, parent);
								ExoSkeletonData componentInChildren2 = transform2.GetComponentInChildren<ExoSkeletonData>();
								componentInChildren2.UpdateAsset(hero2.exoSkeletonDataAsset);
								componentInChildren2.SetAnimFrame("Portrait", "StandStatic", 0);

								Sprite diceChallengeSprite = CharacterAPI.ResolvePortrait(hero2.unitDefinition.uniqueId, "CHALLENGE");
								if (diceChallengeSprite != null)
								{
									PortraitPatches.ReplaceWithFlatImage(componentInChildren2.gameObject, diceChallengeSprite, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
								}

								Image component5 = transform2.FindDeepChild("Image").GetComponent<Image>();
								if (diceResult.kind == DiceChallenge.DiceResult.DiceKind.Glorious)
								{
									component5.sprite = __instance.gloriousFrame;
								}
								if (diceResult.kind == DiceChallenge.DiceResult.DiceKind.Doom)
								{
									component5.sprite = __instance.doomFrame;
								}
								transform2.gameObject.SetActive(true);
							}
							goto AddResponse;
						}
						if (responseInfo.challenge is RequiredItemChallenge)
						{
							RequiredItemChallenge requiredItemChallenge = (RequiredItemChallenge)responseInfo.challenge;
							Transform transform3 = component.transform.FindDeepChild("WhichItem");
							transform3.gameObject.SetActive(true);
							Image component4b = transform3.GetComponent<Image>();
							Image component5b = transform3.FindDeepChild("ItemIcon").GetComponent<Image>();
							Color color = responseInfo.enabled ? Color.white : Color.gray;
							component4b.color = color;
							component5b.color = color;
							ItemDefinition itemDefinition = MetagameManager.instance.InventoryManager.GetItemDefinition(requiredItemChallenge.itemId);
							Sprite spriteByName = __instance.itemIconsDB.GetSpriteByName(itemDefinition.icon);
							component5b.sprite = spriteByName;
						}
						else if (responseInfo.challenge is RequiredLootChallenge)
						{
							RequiredLootChallenge requiredLootChallenge = (RequiredLootChallenge)responseInfo.challenge;
							Transform transform4 = component.transform.FindDeepChild("Loot");
							transform4.gameObject.SetActive(true);
							Image component6 = transform4.GetComponent<Image>();
							TextMeshProUGUI component7 = transform4.FindDeepChild("Number").GetComponent<TextMeshProUGUI>();
							Color color2 = responseInfo.enabled ? Color.white : Color.gray;
							component6.color = color2;
							component7.color = color2;
							LocalizationComponent.SetFinalText(component7, requiredLootChallenge.amount.ToString(), false);
						}
					}
					AddResponse:
					__instance.responses.Add(component);
				}
				traverse.Field<int>("lineIndex").Value = 0;
				traverse.Field<int>("pageIndex").Value = 0;
				linesPerPages.Clear();
			}
			else
			{
				traverse.Field<bool>("mustClearBallonsStack").Value = false;
				DialogViewResponseButton original = clickToContinue;
				Dialog currentDialog2 = MetagameManager.instance.DialogManager.CurrentDialog;
				if ((currentDialog2.currentNode.exit && array != null && lineIndex == array.Length - 1 && pageIndex == linesPerPages.Count - 1) || (currentDialog2.currentNode.exit && array == null))
				{
					original = UnityEngine.Object.Instantiate(clickToEndField, __instance.responseContainer).GetComponent<DialogViewResponseButton>();
				}
				if (array == null || (lineIndex == array.Length - 1 && pageIndex == linesPerPages.Count - 1))
				{
					__instance.ProcessChallengeCheat();
				}
				DialogViewResponseButton component8 = UnityEngine.Object.Instantiate(original, __instance.responseContainer).GetComponent<DialogViewResponseButton>();
				component8.gameObject.SetActive(currentDialog.challenge == null || (currentDialog.challenge != null && currentDialog.challenge.GetType() != typeof(DiceChallenge)) || (currentDialog.challenge != null && currentDialog.challenge.GetType() == typeof(DiceChallenge) && currentDialog.challenge.processedResult));
				component8.index = -1;
				if (array == null)
				{
					component8.callback = handleContinue;
					traverse.Field<int>("lineIndex").Value = 0;
					traverse.Field<int>("pageIndex").Value = 0;
					linesPerPages.Clear();
				}
				else
				{
					component8.callback = handleContinueMultiLines;
				}
				__instance.responses.Add(component8);
			}

			foreach (DialogViewResponseButton dialogViewResponseButton in __instance.responses)
			{
				if (dialogViewResponseButton.callback != new Action<int>(__instance.HandleNotEnabledResponse))
				{
					dialogViewResponseButton.GetComponent<Button>().Select();
					traverse.Field<GameObject>("firstSelected").Value = dialogViewResponseButton.gameObject;
					break;
				}
			}

			return false;
		}
	}
}
