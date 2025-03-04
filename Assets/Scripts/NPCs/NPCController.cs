﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using ExtensionMethods;
using System;

[DisallowMultipleComponent]
[RequireComponent(typeof(UniqueId))]
[RequireComponent(typeof(_ElectricListener))]
public class NPCController : MonoBehaviour, ISavable {

	[HideInInspector]
	public List<Dialog> dialogs = new List<Dialog>();
	
	public GameObject dialogPrefab;
	public Transform headBone;
	public float headRange = 12;
	public bool ignoreY = true;
	public bool lookAtWhileIdle = true;
	public Vector3 idleAngle;
	public float forwardAngle;
	[HideInInspector]
	public AnimationCurve headWeight = new AnimationCurve(new Keyframe(0, 1), new Keyframe(90, 1), new Keyframe(180, .5f));

	public int dialogID = 0;
	private NPCDialogBox dialogUI;
	private int currentDialog = -1;
	private List<int> shufflelist = new List<int>();

	public bool isTalking { get { return currentDialog != -1; } }

#if UNITY_EDITOR
	void OnDrawGizmosSelected() {
		// Gizmos displaying range of the head rotating
		if (ignoreY) {
			Vector3 pos = transform.position.SetY(0);
			float rad = headRange;

			UnityEditor.Handles.color = Color.red;
			UnityEditor.Handles.DrawLine(pos + new Vector3(rad, 50), pos + new Vector3(rad, -50));
			UnityEditor.Handles.DrawLine(pos + new Vector3(-rad, 50), pos + new Vector3(-rad, -50));
			UnityEditor.Handles.DrawLine(pos + new Vector3(0, 50, rad), pos + new Vector3(0, -50, rad));
			UnityEditor.Handles.DrawLine(pos + new Vector3(0, 50, -rad), pos + new Vector3(0, -50, -rad));
			UnityEditor.Handles.DrawWireDisc(pos, Vector3.up, rad);
			UnityEditor.Handles.DrawWireDisc(pos + Vector3.up * 50, Vector3.up, rad);
			UnityEditor.Handles.DrawWireDisc(pos + Vector3.down * 50, Vector3.up, rad);
		} else {
			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, headRange);
		}

		if (headBone != null) {
			// Arrow for idle angle
			UnityEditor.Handles.color = Color.green;
			UnityEditor.Handles.ArrowHandleCap(-1, headBone.position, Quaternion.Euler(idleAngle), 1.5f, EventType.ignore);
			// Arrow for forward angle
			UnityEditor.Handles.color = Color.blue;
			UnityEditor.Handles.ArrowHandleCap(-1, headBone.position, Quaternion.Euler(0, forwardAngle, 0), 1, EventType.ignore);
		}
	}
#endif

	#region Transform algorithms
	void Update() {
		Quaternion lookDirection = LockRotation(GetRotation());

		float cy = headBone.eulerAngles.y - forwardAngle;
		float dy = lookDirection.eulerAngles.y - forwardAngle;
		float ny = Mathf.Lerp(cy, dy, Time.deltaTime * 5) + forwardAngle;

		headBone.rotation = Quaternion.Lerp(headBone.rotation, lookDirection, Time.deltaTime * 5);
		headBone.eulerAngles = new Vector3(headBone.eulerAngles.x, ny, headBone.eulerAngles.z);
	}

	Quaternion LockRotation(Quaternion rotation) {
		Vector3 euler = rotation.eulerAngles;

		// Lock it on the Y axis
		var dAngle = Mathf.DeltaAngle(euler.y, forwardAngle);
		float weight = Mathf.Clamp(headWeight.Evaluate(Mathf.Abs(dAngle)), 0f, 1f); // abs(-180 to 180) => 0 to 180
		euler.y = Mathf.LerpAngle(euler.y, forwardAngle, 1 - weight);

		// TODO: Lock the tilt too

		return Quaternion.Euler(euler);
	}

	Quaternion GetRotation() {
		if ((!isTalking && lookAtWhileIdle)
		|| (isTalking && dialogs[currentDialog].current.turnHead)) {
			var player = PlayerController.instance;

			if (player != null) {
				// Just look, dont mind checking if player is in range while talking
				if (isTalking)
					return Quaternion.LookRotation(player.characterTop - headBone.position);
				
				// Check the distance to the player
				Vector3 a = player.transform.position;
				Vector3 b = transform.position;
				if (ignoreY) a.y = b.y = 0;
				if (Vector3.Distance(a, b) <= headRange) {
					return Quaternion.LookRotation(player.characterTop - headBone.position);
				}
			}
		}

		return Quaternion.Euler(idleAngle);
	}
	#endregion

	#region Dialog algorithms
	void OnInteractStart(PlayerController source) {
		if (!source) return;

		// Freeze the players movement
		if (source.interaction) source.interaction.talkingTo = this;
		if (source.movement) source.movement.body.velocity = Vector3.zero;

		if (dialogUI && !dialogUI.done) {
			// Skip the dialog animation
			dialogUI.SkipAnimation();
		} else {
			// Goto next dialog

			string message = GetNextMessage();

			if (message == null) {
				// Done talking
				if (dialogUI)
					Destroy(dialogUI.gameObject);
				// Release the player
				if (source.interaction) source.interaction.talkingTo = null;
			} else {
				if (dialogUI)
					// Tell the existing one to keep talking
					dialogUI.NewDialog(message);
				else
					// Create a new one
					CreateNewDialog(message);
			}
		}
	}

	void CreateNewDialog(string dialog) {
		Canvas canvas = FindObjectOfType<Canvas>();

		if (canvas) {
			// Create object from prefab
			GameObject clone = Instantiate(dialogPrefab) as GameObject;
			clone.transform.SetParent(canvas.transform);
			
			// Assign variables
			NPCDialogBox box = clone.GetComponent<NPCDialogBox>();
			box.target = headBone ?? transform;
			box.dialog = dialog;

			// Save for later referance
			dialogUI = box;
		} else {
			Debug.LogError("No canvas found! Please make sure one is in the scene");
		}
	}

	public Vector3 GetAxis(Vector3 from) {
		Vector3 vec = transform.position - from;
		return new Vector3(vec.x, 0, vec.z).normalized;
	}

	string GetNextMessage() {

		// Find the first dialog
		if (currentDialog == -1)
			currentDialog = dialogs.FindIndex(d => d.playOnce && d.nextIndex != -1);

		// No more playonces left
		if (currentDialog == -1) {
			if (shufflelist.Count == 0) {
				// Make a new random list
				for (int index = 0; index < dialogs.Count; index++) {
					if (!dialogs[index].playOnce)
						shufflelist.Add(index);
				}
				shufflelist.Shuffle();
			}

			if (shufflelist.Count != 0)
				// Pop a dialog from the list
				currentDialog = shufflelist.Pop();
		}

		// Get it's string
		if (currentDialog != -1) {
			string str;

			dialogs[currentDialog] = dialogs[currentDialog].Next(out str);
			if (str == null) currentDialog = -1;

			return str;
		}

		// No other conversations
		return null;
	}
	#endregion

	#region Saving
	public void OnSave(ref Dictionary<string, object> data) {
		data["npc@dialog_list"] = dialogs;
		data["npc@dialog_shuffle"] = shufflelist;
		data["npc@dialog_id"] = dialogID;
	}

	public void OnLoad(Dictionary<string, object> data) {
		dialogs = (List<Dialog>)data["npc@dialog_list"];
		shufflelist = (List<int>)data["npc@dialog_shuffle"];
		dialogID = (int)data["npc@dialog_id"];
	}
	#endregion

	public void Override(List<Dialog> newDialog, int id) {
		if (id == dialogID) {
			print("dont override for " + name);
			return;
		}
		print("override for " + name);

		this.dialogs = CopyDialog(newDialog);
		currentDialog = -1;
		shufflelist.Clear();
		dialogID = id;
	}

	public static List<Dialog> CopyDialog(List<Dialog> list) {
		var copy = new List<Dialog>();
		for (int i = 0; i < list.Count; i++) {
			var dialog = list[i];

			copy.Add(new Dialog {
				messages = new List<Dialog.Message>(dialog.messages),
				playOnce = dialog.playOnce,
			});
		}
		return copy;
	}

	[System.Serializable]
	public struct Dialog {
		public List<Message> messages;
		public bool playOnce;
		public int nextIndex;
		public int currIndex;

		public Message current {
			get { return messages[currIndex]; }
		}

		public Dialog Next(out string msg) {
			msg = null;
			currIndex = nextIndex;

			// Error check
			if (nextIndex == -1) return this; // done talking
			if (nextIndex >= messages.Count) { // prepare for next chat
				nextIndex = 0;
				return this;
			}

			// Get message
			msg = messages[nextIndex].text;

			// Iterate
			nextIndex++;

			if (nextIndex >= messages.Count) {
				if (playOnce)
					nextIndex = -1;
				else
					nextIndex = messages.Count;
			}

			return this;
		}

		[System.Serializable]
		public struct Message {
			[TextArea]
			public string text;
			public bool turnHead;
		}
	}
	
}

