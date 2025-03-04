﻿using UnityEngine;
using System.Collections;
using ExtensionMethods;

public class PlayerMovement : PlayerSubClass {

	[Header("Object references")]

	public Rigidbody body;
	public CapsuleCollider capsule;

	[Header("Movement settings")]

	[Tooltip("Speed in meters per second.")]
	public float moveSpeed = 5;
	[Tooltip("Time in seconds it takes to reach speed of /moveSpeed/. The lower the value the higer the acceleration.")]
	public float topSpeedAfter = 0.5f;
	[Tooltip("Speed in degrees per second.")]
	public float rotSpeed = 300;
	public float jumpHeight = 5;
	[Tooltip("Should the player be able to hold space and continue jumping?")]
	public bool continuousJumping = true;

	[Header("Ground raycasting")]

	public float groundDist = 1f;
	public LayerMask groundLayer;
	[Range(0,90)]
	public float slopeLimit = 30f;
	public float slopeForce = 1f;
	
	[HideInInspector]
	public Vector3 outsideMotion;

	[System.NonSerialized]
	public Vector3? autoMoveTowards;
	[System.NonSerialized]
	public int autoMoveID = -1;
	
	[HideInInspector]
	public bool grounded;
	
	private RaycastHit? lastHit;

	[HideInInspector]
	public _Platform platform;

#if UNITY_EDITOR
	void OnDrawGizmosSelected() {

		Gizmos.color = Color.green;
		Gizmos.DrawRay(transform.position + Vector3.up * groundDist, Vector3.down * (lastHit.HasValue ? lastHit.Value.distance : groundDist*2));
		if (lastHit.HasValue) {
			Gizmos.color = Color.red;
			Gizmos.DrawRay(lastHit.Value.point, Vector3.down * (groundDist * 2 - lastHit.Value.distance));
		}
	}
#endif

	void FixedUpdate() {
		if (health.dead)
			// Stop movement when dead
			return;

		RaycastGround();
	}

	void Update() {
		if (health.dead)
			// Stop movement when dead
			return;

		Move();
		Rotate();
	}

	#region Movement algorithms
	void OnCollisionStay(Collision col) {
		//float mult = 1 / col.contacts.Length;
		//foreach (ContactPoint contact in col.contacts) {
		//	var angle = Vector3.Angle(contact.normal, Vector3.up);
		//	if (angle > slopeLimit) {
		//		body.velocity += contact.normal.SetY(0).normalized * slopeForce * mult * Time.fixedDeltaTime;
		//	}
		//}
	}

	void RaycastGround() {
		RaycastHit hit;
		bool old_grounded = grounded;

		if (Physics.Raycast(transform.position + Vector3.up * groundDist, Vector3.down, out hit, groundDist*2, groundLayer)) {
			grounded = Vector3.Angle(hit.normal, Vector3.up) <= slopeLimit;

			// Push away from the cliff
			//outsideMotion = hit.normal.SetY(0) * slopeForce * (1 - Mathf.Abs(hit.normal.y));

			GameObject main = hit.collider.attachedRigidbody ? hit.collider.attachedRigidbody.gameObject : hit.collider.gameObject;
			
			// Send touch events
			_TouchListener listener = main.GetComponent<_TouchListener>();
			if (listener) listener.Touch(this);

			// Get the current platform
			platform = main.GetComponent<_Platform>();

			lastHit = hit;
		} else {
			outsideMotion = Vector3.zero;
			grounded = false;
			platform = null;
			lastHit = null;
		}

		// /grounded/ changed
		if (old_grounded != grounded) {
			if (grounded && sound) sound.OnGrounded();
		}
	}

	private Vector3 lastOutsideMotion;
	void Move() {
		// Motion to apply to the character
		Vector3 motion = Vector3.zero;

		if (hud.isOpen)
			// Dont move if inventory open
			motion = Vector3.zero;
		else if (pushing && pushing.point != null)
			// Move according to the pushing point
			motion = pushing.GetMovement();
		else if (interaction && interaction.talkingTo)
			// Dont move if talking to a NPC
			motion = Vector3.zero;
		else if (autoMoveTowards.HasValue)
			motion = (autoMoveTowards.Value - transform.position).SetY(0).normalized * moveSpeed;
		else
			// Use the players input for moving
			motion = GetAxis() * moveSpeed;

		// Less control if airborne
		if (!grounded)
			motion = Vector3.Lerp(motion, body.velocity - outsideMotion, 0.975f);

		// Apply acceleration to motion vector
		motion = Vector3.MoveTowards(body.velocity - lastOutsideMotion, motion, moveSpeed * Time.deltaTime / topSpeedAfter);

		if (grounded) {
			// Jumping
			if (ShouldJump())
				motion.y = jumpHeight;
		} else {
			// Apply gravity
			motion.y = body.velocity.y;
		}

		// Move the character
		//character.Move((motion + outsideForces) * Time.deltaTime);
		body.velocity = motion + outsideMotion;
		lastOutsideMotion = outsideMotion;
	}

	void Rotate() {
		// Vector of the (looking) axis
		Vector3 rawAxis;

		if (hud.isOpen)
			// Dont rotate
			rawAxis = Vector3.zero;
		else if (pushing && pushing.point != null)
			// Turn according to pushing point
			rawAxis = pushing.GetAxis();
		else if (interaction && interaction.talkingTo != null)
			// Turn towards NPC
			rawAxis = interaction.talkingTo.GetAxis(from:transform.position);
		else if (autoMoveTowards.HasValue) {
			rawAxis = (autoMoveTowards.Value - transform.position).SetY(0).normalized;
		} else {
			// Listen to users input
			rawAxis = new Vector3(Input.GetAxisRaw("HorizontalLook"), 0, Input.GetAxisRaw("VerticalLook"));

			// Not using the looking axis input, try the movement axis
			if (rawAxis.x == 0 && rawAxis.z == 0)
				rawAxis = new Vector3(Input.GetAxisRaw("HorizontalMove"), 0, Input.GetAxisRaw("VerticalMove"));
		}

		// No rotation
		if (rawAxis.x == 0 && rawAxis.z == 0) {
			body.angularVelocity = Vector3.zero;
			return;
		}

		// Get the angles
		Vector3 rot = transform.eulerAngles;
		float angle = Mathf.Atan2(rawAxis.z, -rawAxis.x) * Mathf.Rad2Deg - 90f;

		// Change the value
		rot.y = Mathf.MoveTowardsAngle(rot.y, angle, rotSpeed * Time.deltaTime);
		// Set the value
		body.MoveRotation(Quaternion.Euler(rot));
		//transform.eulerAngles = rot;
	}

	public Vector3 GetAxis() {
		// Vector of the (movement) axis
		Vector3 inputAxis = new Vector3(Input.GetAxis("HorizontalMove"), 0, Input.GetAxis("VerticalMove"));

		// Normalize it so that all directions moves the same combined speed
		Vector3 axis = inputAxis.normalized;

		// Restore the movement lerp that's lost in the normalization
		axis.x *= Mathf.Abs(inputAxis.x);
		axis.z *= Mathf.Abs(inputAxis.z);

		return axis;
	}

	// This is combined with the grounded field
	bool ShouldJump() {
		return !pushing.hasPoint && ((continuousJumping && Input.GetButton("Jump"))
			|| (!continuousJumping && Input.GetButtonDown("Jump")));
	}
	#endregion
}
