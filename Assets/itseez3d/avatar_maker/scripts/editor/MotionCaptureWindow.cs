/* Copyright (C) Itseez3D, Inc. - All Rights Reserved
* You may not use this file except in compliance with an authorized license
* Unauthorized copying of this file, via any medium is strictly prohibited
* Proprietary and confidential
* UNLESS REQUIRED BY APPLICABLE LAW OR AGREED BY ITSEEZ3D, INC. IN WRITING, SOFTWARE DISTRIBUTED UNDER THE LICENSE IS DISTRIBUTED ON AN "AS IS" BASIS, WITHOUT WARRANTIES OR
* CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED
* See the License for the specific language governing permissions and limitations under the License.
* Written by Itseez3D, Inc. <support@itseez3D.com>, January 2019
*/

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ItSeez3D.AvatarMaker.WebCamera;
using ItSeez3D.AvatarMaker.MotionCapture;
using System.IO;
using System;

namespace ItSeez3D.AvatarMaker.Editor
{
	public class MotionCaptureWindow : BaseWindow
	{
		class SliderWithTextValues
		{
			public SliderWithTextValues(float value)
			{
				sliderValue = value;
				textValue = value.ToString();
			}

			public void SetValue(float value)
			{
				sliderValue = value;
				textValue = value.ToString();
			}

			public float sliderValue;
			public string textValue;
		}


		private AnimationClip animationClip = null;
		private bool isCapturing = false;
		private string capturingButtonLabel = string.Empty;
		private string recordingButtonLabel = string.Empty;
		private string capturingErrorLabel = string.Empty;
		private string recordingErrorLabel = string.Empty;
		private string avatarErrorLabel = string.Empty;
		private float animationTime = 0.0f;
		private bool isAutoPlayAnimation = true;
		private int cameraId = 0;

		private Dictionary<string, SliderWithTextValues> blendshapesImpactControls = new Dictionary<string, SliderWithTextValues>();
		private Dictionary<string, float> blendshapesImpactValues = new Dictionary<string, float>(); 
		private bool showBlendshapeCoefficients = false;
		private readonly float maxImpactValue = 3f;

		private AvatarInfo avatarInfo = null;

		private AvatarAnimator avatarAnimator = null;

		private Vector2 scrollPosition;
		private Vector2 blendshapesScrollPosition;

		[MenuItem("Window/Avatar Maker/Facial Motion Capture")]
		static void Init()
		{
			var window = (MotionCaptureWindow)EditorWindow.GetWindow(typeof(MotionCaptureWindow));
			window.titleContent.text = "Facial Capture";
			window.minSize = new Vector2(480, 550);
			window.Show();
		}

		private void OnEnable()
		{
			if (!AvatarMakerInitializer.IsPlatformSupported())
			{
				Debug.LogError("Avatar plugin supports only Windows platform and works in the Editor mode.");
				return;
			}

			if (!AvatarMakerInitializer.IsInitialized)
				AvatarMakerInitializer.StartInitialization();

#if UNITY_2018_1_OR_NEWER
			EditorApplication.hierarchyChanged += OnHierarchyChanged;
#else
			EditorApplication.hierarchyWindowChanged += OnHierarchyChanged;
#endif
			OnHierarchyChanged();
		}

		void OnGUI()
		{
			InitUI();

			GUILayout.Label("Facial Motion Capture", titleStyle);
			GUILayout.Space(20);

			if (!AvatarMakerInitializer.IsPlatformSupported())
			{
				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				GUILayout.Label("Avatar plugin supports only Windows platform and works in the Editor mode.");
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				return;
			}

			if (AvatarMakerInitializer.IsInitializationInProgress)
			{
				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				GUILayout.Label(EditorApplication.isPlaying ? "Exit play mode to load SDK" : "Loading...");
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				return;
			}

			if (!AvatarMakerInitializer.IsMotionCaptureSupported)
			{
				EditorGUILayout.HelpBox("Your CPU doesn't have AVX extension required for Motion Tracking.", MessageType.Error);
				return;
			}

			if (avatarInfo == null)
			{
				if (isCapturing)
				{
					avatarAnimator.StopCapturing();
					avatarAnimator = null;
					isCapturing = false;
				}

				if (AnimationMode.InAnimationMode())
					ToggleAnimationMode();

				EditorGUILayout.HelpBox(avatarErrorLabel, MessageType.Info);
				return;
			}

			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			WebCamDevice[] cameraDevices = WebCamTexture.devices;
			if (cameraDevices != null && cameraDevices.Length > 0)
			{
				if (!isCapturing)
				{
					if (cameraDevices.Length > 1)
					{
						string[] cameraNames = cameraDevices.Select(d => { return d.name; }).ToArray();
						cameraId = GUILayout.SelectionGrid(cameraId, cameraNames, 1, "toggle");
					}
					else
						cameraId = 0;
				}

				EditorGUI.BeginDisabledGroup(avatarInfo == null);
				capturingButtonLabel = isCapturing ? "Stop capturing" : "Start capturing";
				isCapturing = GUILayout.Toggle(isCapturing, capturingButtonLabel, "Button");
				EditorGUI.EndDisabledGroup();
			}
			else
			{
				EditorGUILayout.HelpBox("There is no available web camera.", MessageType.Info);
			}

			if (isCapturing)
			{
				capturingErrorLabel = string.Empty;
				if (avatarAnimator == null)
				{
					avatarAnimator = new AvatarAnimator(avatarInfo.transform, avatarInfo.headMeshRenderer, cameraOffset);
					isCapturing = avatarAnimator.StartCapturing(WebCamTexture.devices[cameraId].name, avatarInfo.code);
					if (!isCapturing)
					{
						capturingErrorLabel = "Unable to start motion capture.";
						Debug.LogError(capturingErrorLabel);
						avatarAnimator = null;
						return;
					}
					ApplyBlendshapesImpact();

					if (AvatarAnimator.RecordAtStart)
						StartRecording();

					if (AnimationMode.InAnimationMode())
						ToggleAnimationMode();
				}
				Texture2D tex = avatarAnimator.HandleCapturedFrame();
				DisplayFrameTexture(tex);
			}
			else
			{
				if (avatarAnimator != null)
				{
					avatarAnimator.StopCapturing();
					avatarAnimator = null;
				}
			}

			if (!string.IsNullOrEmpty(capturingErrorLabel))
				EditorGUILayout.HelpBox(capturingErrorLabel, MessageType.Error);
			GUILayout.Space(20);

			EditorGUILayout.BeginVertical("Box");
			{
				EditorGUILayout.LabelField("Recording options", titleStyle);
				GUILayout.Space(5);
				if (isCapturing)
				{
					recordingButtonLabel = avatarAnimator.IsRecording ? "Stop recording" : "Start recording";
					if (avatarAnimator.IsRecording != GUILayout.Toggle(avatarAnimator.IsRecording, recordingButtonLabel, "Button"))
					{
						if (avatarAnimator.IsRecording)
							avatarAnimator.FinishRecording();
						else
							StartRecording();
					}
					GUILayout.Space(5);
				}

				AvatarAnimator.RecordAtStart = GUILayout.Toggle(AvatarAnimator.RecordAtStart, "Record at start");

				animationClip = (AnimationClip)EditorGUILayout.ObjectField("Animation file: ", animationClip, typeof(AnimationClip), false);

				AvatarAnimator.ApplyTranslation = GUILayout.Toggle(AvatarAnimator.ApplyTranslation, "Capture translation");
				AvatarAnimator.ApplyRotation = GUILayout.Toggle(AvatarAnimator.ApplyRotation, "Capture rotation");
			}
			EditorGUILayout.EndVertical();
			GUILayout.Space(10);

			if (animationClip != null && !isCapturing)
			{
				EditorGUILayout.BeginVertical("Box");
				{
					EditorGUILayout.LabelField("Playback", titleStyle);
					GUILayout.Space(5);

					EditorGUI.BeginChangeCheck();
					GUILayout.Toggle(AnimationMode.InAnimationMode(), "Play recorded animation");
					if (EditorGUI.EndChangeCheck())
						ToggleAnimationMode();

					if (AnimationMode.InAnimationMode())
					{
						isAutoPlayAnimation = GUILayout.Toggle(isAutoPlayAnimation, "Automatically play in loop");

						animationTime = EditorGUILayout.Slider(animationTime, 0.0f, animationClip.length);
					}
				}
				EditorGUILayout.EndVertical();
				GUILayout.Space(10);
			}

			if (!string.IsNullOrEmpty(recordingErrorLabel))
			{
				EditorGUILayout.HelpBox(recordingErrorLabel, MessageType.Error);
				ShowAvatarMakerProLink();
			}
			GUILayout.Space(10);

			showBlendshapeCoefficients = EditorGUILayout.Foldout(showBlendshapeCoefficients, "Blendshapes Impact");
			if (showBlendshapeCoefficients)
			{
				if (blendshapesImpactControls.Count == 0)
				{
					List<string> blendshapeNames = MotionCapturer.GetBlendshapesNames();
					for (int i = 0; i < blendshapeNames.Count; i++)
					{
						blendshapesImpactControls.Add(blendshapeNames[i], new SliderWithTextValues(1.0f));
						blendshapesImpactValues.Add(blendshapeNames[i], 1f);
					}
				}

				blendshapesScrollPosition = GUILayout.BeginScrollView(blendshapesScrollPosition, GUILayout.Height(Mathf.Max(200f, position.height - 200f)));
				var blendshapesNames = blendshapesImpactControls.Keys.ToList<string>();
				for (int i = 0; i < blendshapesNames.Count; i++)
				{
					SliderWithTextValues controlsValues = blendshapesImpactControls[blendshapesNames[i]];
					
					GUILayout.BeginHorizontal();
					GUILayout.Label(blendshapesNames[i] + ":", GUILayout.MaxWidth(100));
					float blendshapeImpactVal = GUILayout.HorizontalSlider(controlsValues.sliderValue, 0, maxImpactValue);
					if (blendshapeImpactVal != controlsValues.sliderValue)
					{
						controlsValues.SetValue(blendshapeImpactVal);
						blendshapesImpactValues[blendshapesNames[i]] = blendshapeImpactVal;
						ApplyBlendshapesImpact();
					}

					string modifiedValueStr = GUILayout.TextField(controlsValues.textValue, GUILayout.Width(100));
					if (modifiedValueStr != blendshapeImpactVal.ToString())
					{
						controlsValues.textValue = modifiedValueStr;
						if (float.TryParse(modifiedValueStr, out blendshapeImpactVal) && blendshapeImpactVal >= 0 && blendshapeImpactVal <= maxImpactValue)
						{
							controlsValues.sliderValue = blendshapeImpactVal;
							blendshapesImpactValues[blendshapesNames[i]] = blendshapeImpactVal;
							ApplyBlendshapesImpact();
						}
					}
					GUILayout.EndHorizontal();
				}
				GUILayout.Space(10);
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Save", buttonSkin))
				{
					SaveBlendshapesImpactValues();
				}
				if (GUILayout.Button("Load", buttonSkin))
				{
					LoadBlendshapesImpactValues();
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(10);
				GUILayout.EndScrollView();
			}

			EditorGUILayout.EndScrollView();

			if (isCapturing)
				Repaint();
		}

		float lastUpdateTime = 0;
		private void Update()
		{
			if (animationClip == null)
				return;

			if (AnimationMode.InAnimationMode())
			{
				if (avatarInfo == null)
				{
					avatarInfo = FindAvatarObject();
					if (avatarInfo == null)
					{
						ToggleAnimationMode();
						return;
					}
				}

				AnimationMode.BeginSampling();
				AnimationMode.SampleAnimationClip(avatarInfo.gameObject, animationClip, animationTime);
				AnimationMode.EndSampling();
				SceneView.RepaintAll();

				if (isAutoPlayAnimation)
				{
					animationTime += (Time.realtimeSinceStartup - lastUpdateTime);
					if (animationTime >= animationClip.length)
						animationTime = 0.0f;
					Repaint();
				}
			}
			lastUpdateTime = Time.realtimeSinceStartup;
		}


		void OnHierarchyChanged()
		{
			avatarInfo = FindAvatarObject();
		}

		private void DisplayFrameTexture(Texture2D cameraTexture)
		{
			float previewAspect = (float)cameraTexture.height / (float)cameraTexture.width;
			int newWidth = (int)Mathf.Min(position.width, Mathf.Min(cameraTexture.width, 360));
			Vector2 previewSize = new Vector2(newWidth, (previewAspect * newWidth));
			GUI.DrawTexture(new Rect(new Vector2((position.width - previewSize.x) * 0.5f, 30.0f), previewSize), cameraTexture);
			GUILayout.Space((int)Mathf.Max(20, previewSize.y + 10));
		}

		private AvatarInfo FindAvatarObject()
		{
			avatarErrorLabel = string.Empty;

			List<UnityEngine.Object> avatarInfoObjects = GameObject.FindObjectsOfType(typeof(AvatarInfo)).ToList();
			if (avatarInfoObjects.Count == 0)
			{
				avatarErrorLabel = "There is no avatar on the scene to animate!";
				return null;
			}
			if (avatarInfoObjects.Count > 1)
			{
				avatarErrorLabel = "There are multiple avatars on the scene! Motion capture works only for a single avatar.";
				return null;
			}

			return avatarInfoObjects[0] as AvatarInfo;
		}

		private void StartRecording()
		{
			if (!avatarAnimator.IsRecordingEnabled)
			{
				recordingErrorLabel = "You are allowed to record animation only in the Avatar Maker Pro version!";
				return;
			}

			if (animationClip == null)
				animationClip = CreateAnimationFile();
			avatarAnimator.StartRecording(animationClip);
		}

		private AnimationClip CreateAnimationFile()
		{
			string animationsFolder = "itseez3d_animations";
			string animationsFolderWithAssets = string.Format("Assets/{0}", animationsFolder);
			if (!AssetDatabase.IsValidFolder(animationsFolderWithAssets))
				AssetDatabase.CreateFolder("Assets", animationsFolder);

			int idx = 0;
			string animationName = string.Empty;
			while(true)
			{
				animationName = string.Format("avatar_animation_{0}", idx);
				if (AssetDatabase.FindAssets(animationName).Length == 0)
					break;
				idx++;
			}

			AnimationClip animation = new AnimationClip();
			string animationFileName = string.Format("{0}/{1}.anim", animationsFolderWithAssets, animationName);
			AssetDatabase.CreateAsset(animation, animationFileName);

			return animation;
		}

		private void ToggleAnimationMode()
		{
			if (AnimationMode.InAnimationMode())
				AnimationMode.StopAnimationMode();
			else
				AnimationMode.StartAnimationMode();
		}

		private void ApplyBlendshapesImpact()
		{
			if (avatarAnimator != null)
				avatarAnimator.SetBlendshapesImpact(blendshapesImpactValues);
		}

		private void SaveBlendshapesImpactValues()
		{
			string filePath = EditorUtility.SaveFilePanel("Blendshapes Impact Values", "", "blendshapes_impact", "txt");
			if (!string.IsNullOrEmpty(filePath))
			{
				List<string> lines = new List<string>();
				foreach (var blendshapesValue in blendshapesImpactValues)
					lines.Add(string.Format("{0}={1}", blendshapesValue.Key, blendshapesValue.Value));
				File.WriteAllLines(filePath, lines.ToArray());
			}
		}

		private void LoadBlendshapesImpactValues()
		{
			string filePath = EditorUtility.OpenFilePanel("Blendshapes Impact Values", "", "txt");
			if (!string.IsNullOrEmpty(filePath))
			{
				string[] lines = File.ReadAllLines(filePath);
				foreach (string line in lines)
				{
					try
					{
						string[] splits = line.Split('=');
						if (splits.Length == 2)
						{
							string name = splits[0];
							float value = float.Parse(splits[1]);
							blendshapesImpactValues[name] = value;
							blendshapesImpactControls[name].SetValue(value);
						}
					}
					catch (Exception exc)
					{
						Debug.LogErrorFormat("Unable parse line {0}: {1}", line, exc);
					}
				}
			}
		}
	}
}
