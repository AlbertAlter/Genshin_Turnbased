/*******************************************************************************
The content of this file includes portions of the proprietary AUDIOKINETIC Wwise
Technology released in source code form as part of the game integration package.
The content of this file may not be used without valid licenses to the
AUDIOKINETIC Wwise Technology.
Note that the use of the game engine is subject to the Unity(R) Terms of
Service at https://unity3d.com/legal/terms-of-service
 
License Usage
 
Licensees holding valid licenses to the AUDIOKINETIC Wwise Technology may use
this file in accordance with the end user license agreement provided with the
software or, alternatively, in accordance with the terms contained
in a written agreement between you and Audiokinetic Inc.
Copyright (c) 2026 Audiokinetic Inc.
*******************************************************************************/

using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Wwise.API.Runtime.WwiseTypes.WwiseObjectsManagers;
using AK.Wwise.Unity.Logging;
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
using AK.Wwise.Unity.WwiseAddressables;
#endif // AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES

/// @brief Represents Wwise events as Unity assets.
[System.Serializable]
public class WwiseEventReference : WwiseObjectReference
#if UNITY_EDITOR
	, AK.Wwise.IMigratable
#endif // UNITY_EDITOR
{
	public override WwiseObjectType WwiseObjectType { get { return WwiseObjectType.Event; } }
	public bool IsInUserDefinedSoundBank = false;
	private uint m_BankID = AkUnitySoundEngine.AK_INVALID_UNIQUE_ID;
#if UNITY_EDITOR
	private bool pendingEnable = false;
#endif
	[System.NonSerialized]
	public bool IsAutoBankLoaded = false;
	[System.NonSerialized]
	private bool isSubscribedToDelegates = false;

#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
	[UnityEngine.SerializeField, AkShowOnly]
	public WwiseAddressableSoundBank AutoBank;
#endif // AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES

	private AkBankTypeEnum BankType
	{
		get
		{
			if (WwiseObjectType == WwiseObjectType.AuxBus)
			{
				return AkBankTypeEnum.AkBankType_Bus;
			}
			else if (WwiseObjectType == WwiseObjectType.Event)
			{
				return AkBankTypeEnum.AkBankType_Event;
			}
			else
			{
				return AkBankTypeEnum.AkBankType_User;
			}
		}
	}

	protected void PostLoadAutoBank(uint bankId)
	{
		if (bankId == AkUnitySoundEngine.AK_INVALID_UNIQUE_ID)
		{
			return;
		}

		var result = AkUnitySoundEngine.PrepareEvent(AkPreparationType.Preparation_Load, new string[] { DisplayName }, 1);
		if (result != AKRESULT.AK_Success)
		{
			WwiseLogger.LogFormat(LogLevel.Error, "PrepareEvent for {0} failed with result: {1}. If the event is in a User Defined Soundbank, make sure to check the \"Is In User-Defined SoundBank\" box in the editor.", DisplayName, result);
		}
	}

#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES

#if UNITY_WEBGL
	public IEnumerator CompleteLoadBank()
	{
		while (!IsAutoBankLoaded && AutoBank.LoadState != BankLoadState.Unloaded)
		{
			yield return null;
		}
	}
#else
	private async Task WaitForAutoBankLoadCompletionAsync(CancellationToken cancellationToken = default)
	{
		while (AutoBank != null && AutoBank.LoadState != BankLoadState.Loaded && AutoBank.LoadState != BankLoadState.LoadFailed && AutoBank.LoadState != BankLoadState.Unloaded)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			await Task.Yield();
		}

		if (AutoBank != null && AutoBank.LoadState == BankLoadState.Loaded)
		{
			while (!IsAutoBankLoaded)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				await Task.Yield();
			}
		}
	}

	//Wait for the Bank to be loaded or a given timeout (in ms)
	public async Task CompleteLoadBank(int timeoutms = 5000)
	{
		using (var cts = new CancellationTokenSource())
		{
			var timeoutTask = Task.Delay(timeoutms, cts.Token);
			var loadTask = WaitForAutoBankLoadCompletionAsync(cts.Token);
			var completedTask = await Task.WhenAny(loadTask, timeoutTask);

			// Cancel the losing task
			cts.Cancel();

			if (completedTask == timeoutTask)
			{
				WwiseLogger.Warning($"CompleteLoadBank timed out after {timeoutms}ms for bank: {DisplayName}. LoadState: {AutoBank?.LoadState}, IsAutoBankLoaded: {IsAutoBankLoaded}");
			}
		}
	}
#endif // UNITY_WEBGL

#if WWISE_ADDRESSABLES_24_1_OR_LATER
	public async Task LoadAutoBankAsync()
	{
		if (AutoBank != null)
		{
			AutoBank.IsAutoBank = !IsInUserDefinedSoundBank;
			AutoBank.BankType = (uint)AkBankTypeEnum.AkBankType_Event;
			AkAddressableBankManager.Instance.LoadBank(AutoBank, false, false, loadAsync:true);
			WwiseEventReferencesManager.Instance.AddReference(this);
		}
		else
		{
			WwiseLogger.LogFormat(LogLevel.Warning, "Wwise Addressable asset for AutoBank: {0} couldn't be found. If the event is in a User-Defined Soundbank, make sure to check the \"Is In User-Defined SoundBank\" box in the editor.", DisplayName);
		}
	}

	public void OnAutoBankLoaded()
	{
		if (AutoBank == null)
		{
			return;
		}
		m_BankID = AutoBank.SoundbankId;
		PostLoadAutoBank(m_BankID);
		AkAddressableBankManager.Instance.OnAutoBankLoaded(AutoBank);
		IsAutoBankLoaded = true;
	}
#endif // WWISE_ADDRESSABLES_24_1_OR_LATER

	private bool ShouldWaitForBankLoad()
	{
#if WWISE_ADDRESSABLES_24_1_OR_LATER
		if (AutoBank == null)
		{
			return false;
		}
		return AutoBank.LoadState == BankLoadState.Loading || AutoBank.LoadState == BankLoadState.WaitingForPrepareEvent;
#else
		return false;
#endif // WWISE_ADDRESSABLES_24_1_OR_LATER
	}

#if UNITY_WEBGL
	public IEnumerator UnloadAutoBankAsync()
	{
		if (ShouldWaitForBankLoad())
		{
			yield return CompleteLoadBank();
		}
		UnloadAutoBank();
	}
#else
	public async Task UnloadAutoBankAsync()
	{
		if (ShouldWaitForBankLoad())
		{
			await CompleteLoadBank();
		}
		UnloadAutoBank();
	}
#endif //UNITY_WEBGL

#if UNITY_EDITOR
	public override void CompleteData()
	{
#if WWISE_ADDRESSABLES_24_1_OR_LATER
		SetAddressableBank(AkAssetUtilities.GetAddressableBankAsset(DisplayName, true));
#else
		SetAddressableBank(AkAssetUtilities.GetAddressableBankAsset(DisplayName));
#endif // WWISE_ADDRESSABLES_24_1_OR_LATER
	}

	public override bool IsComplete()
	{
		return (AutoBank != null && IsInUserDefinedSoundBank == false) || (AutoBank == null && IsInUserDefinedSoundBank == true);
	}

	public void SetAddressableBank(WwiseAddressableSoundBank asset)
	{
		AutoBank = asset;
		IsInUserDefinedSoundBank = asset == null;
		EditorUtility.SetDirty(this);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	public static void FindEventReferenceAndSetAddressableBank(WwiseAddressableSoundBank addressableAsset, string name)
	{
		var guids = UnityEditor.AssetDatabase.FindAssets("t:" + typeof(WwiseEventReference).Name);
		WwiseEventReference asset;
		foreach (var assetGuid in guids)
		{
			var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
			asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WwiseEventReference>(assetPath);
			if (asset && asset.ObjectName == name)
			{
				asset.SetAddressableBank(addressableAsset);
			}
		}
	}
#endif // UNITY_EDITOR

#else // !AK_WWISE_ADDRESSABLES || !UNITY_ADDRESSABLES

	public void LoadAutoBankAsync()
	{
		m_BankID = AkBankManager.LoadBank(DisplayName, false, false, BankType);
		WwiseEventReferencesManager.Instance.AddReference(this);
		PostLoadAutoBank(m_BankID);
		IsAutoBankLoaded = true;
	}

#endif // AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES

	public void LoadAutoBank()
	{
		if (IsInUserDefinedSoundBank || !AkWwiseInitializationSettings.Instance.IsAutoBankEnabled)
		{
			return;
		}

#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
#if WWISE_ADDRESSABLES_24_1_OR_LATER
		if (AutoBank != null)
		{
			if (AutoBank.LoadState == BankLoadState.Loading)
			{
				return;
			}
			AutoBank.OnBankLoaded += OnAutoBankLoaded;
		}

		_ = LoadAutoBankAsync();
#else
		WwiseLogger.LogFormat(LogLevel.Error, "Auto Bank is not supported with Addressables prior to the Wwise Addressables Package 24.1. Please update your wwise Addressables package or add the event: {0} to an user-defined soundbank in Wwise Authoring.", DisplayName);
#endif //WWISE_ADDRESSABLES_24_1_OR_LATER
#else
		LoadAutoBankAsync();
#endif // AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
	}

	public void ReloadAutoBank()
	{
		UnloadAutoBank();
#if UNITY_EDITOR
		//Can be triggered from going in and out of PIE.
		if (!Application.isPlaying)
		{
			//Asset fails to load when is not playing (in editor).
			return;
		}
#endif // UNITY_EDITOR
		LoadAutoBank();
	}

	public void UnloadAutoBank()
	{
		WwiseLogger.Verbose($"UnloadAutoBank started for : {this.DisplayName}");
		
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES && WWISE_ADDRESSABLES_24_1_OR_LATER
		if (AutoBank != null)
		{
			AutoBank.OnBankLoaded -= OnAutoBankLoaded;
		}
#endif // AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES && WWISE_ADDRESSABLES_24_1_OR_LATER
		
		if (m_BankID != AkUnitySoundEngine.AK_INVALID_UNIQUE_ID && IsAutoBankLoaded)
		{
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
			AkAddressableBankManager.Instance.UnloadBank(AutoBank, true);
#else
			AkUnitySoundEngine.PrepareEvent(AkPreparationType.Preparation_Unload, new string[] { DisplayName }, 1);
			AkBankManager.UnloadBank(DisplayName);
#endif // AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
			m_BankID = AkUnitySoundEngine.AK_INVALID_UNIQUE_ID;
			WwiseEventReferencesManager.Instance.RemoveReference(this);
		}
		IsAutoBankLoaded = false;
	}

	public void UnloadAutoBankOnSoundEngineTermination()
	{
		UnloadAutoBank();
	}

	private void EnableSequence()
	{
		if (isSubscribedToDelegates)
		{
			return;
		}
#if UNITY_EDITOR
		WwiseProjectDatabase.SoundBankDirectoryUpdated += UpdateIsUserDefinedSoundBank;
#endif // UNITY_EDITOR
		if (AkUnitySoundEngine.IsInitialized())
		{
			LoadAutoBank();
		}
		else
		{
			AkUnitySoundEngineInitialization.Instance.initializationDelegate += LoadAutoBank;
		}
		AkUnitySoundEngineInitialization.Instance.reInitializationDelegate += ReloadAutoBank;
		AkUnitySoundEngineInitialization.Instance.terminationDelegate += UnloadAutoBankOnSoundEngineTermination;
		
		isSubscribedToDelegates = true;
	}

	private void OnEnable()
	{
#if UNITY_EDITOR
		//Do not load the bank if the editor is not currently playing. In order to get the information from an AkEvent, the associated Scriptable Object has to be loaded which would otherwise trigger the load of the bank.
		if (Application.isEditor && !EditorApplication.isPlayingOrWillChangePlaymode)
		{
			pendingEnable = true;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			return;
		}
#endif // UNITY_EDITOR
		EnableSequence();
	}

	public void OnDisable()
	{
		UnloadAutoBank();
		if (isSubscribedToDelegates)
		{
			AkUnitySoundEngineInitialization.Instance.initializationDelegate -= LoadAutoBank;
			AkUnitySoundEngineInitialization.Instance.reInitializationDelegate -= ReloadAutoBank;
			AkUnitySoundEngineInitialization.Instance.terminationDelegate -= UnloadAutoBankOnSoundEngineTermination;
			isSubscribedToDelegates = false;
		}
#if UNITY_EDITOR
		WwiseProjectDatabase.SoundBankDirectoryUpdated -= UpdateIsUserDefinedSoundBank;
#endif // UNITY_EDITOR
	}

#if UNITY_EDITOR
	private void OnPlayModeStateChanged(PlayModeStateChange state)
	{
		if (pendingEnable && (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode))
		{
			EnableSequence();
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			pendingEnable = false;
		}
	}

	public void UpdateIsUserDefinedSoundBank()
	{
		if (WwiseProjectDatabase.ProjectInfoExists && WwiseProjectDatabase.IsInitialized)
		{
			WwiseSoundBankRef soundBankRef = new WwiseSoundBankRef(DisplayName, "Event");
			
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
			if (!soundBankRef.IsValid && AutoBank != null)
			{
				foreach (var data in AutoBank.Data)
				{
					WwiseProjectDatabase.SetCurrentLanguage(data.Key);
					soundBankRef = new WwiseSoundBankRef(DisplayName, "Event");
					if (soundBankRef.IsValid)
						break;
				}
			}
#endif
			if (IsInUserDefinedSoundBank == !soundBankRef.IsValid)
			{
				return;
			}
			IsInUserDefinedSoundBank = !soundBankRef.IsValid;
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
			if (IsInUserDefinedSoundBank && AutoBank != null)
			{
				AutoBank = null;
			}
#endif
			UnityEditor.EditorUtility.SetDirty(this);
			if (IsAutoBankLoaded)
			{
				UnloadAutoBank();
			}
			LoadAutoBank();
		}
	}

	bool AK.Wwise.IMigratable.Migrate(UnityEditor.SerializedObject serializedObject)
	{
		// If the object was existing before the introduction of Auto-Defined SoundBanks,
		// it has to be in a user-defined SoundBank.
		var userDefinedProperty = serializedObject.FindProperty("IsInUserDefinedSoundBank");
		if (userDefinedProperty != null)
		{
			userDefinedProperty.boolValue = true;
			return true;
		}
		return false;
	}

#endif // UNITY_EDITOR
}
