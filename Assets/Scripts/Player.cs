﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour {
	// settings:
	[Header("Jump")]
	public float jumpSpeed;
	public int coyoteFrames, jumpAheadFrames;

	[Header("Movement")]
	public float moveSpeed;
	public float airAccelerationTime;
	[Range(0, 1f)]
	public float groundFriction = 0.05f;

	[Header("Wall jump")]
	[Range(0, 1f)]
	public float wallSlideDamping = 0.9f;
	public float wallJumpAngle;
	public float wallJumpDuration = 0.2f;

	// constants:
	private const float LOOK_AHEAD_DIST = 0.01f;
	private const float MIN_GROUND_NORMAL_Y = 0.65f;

	// state:
	private Vector2 velocity;
	private float velocityXRef;

	private bool grounded;
	private Vector2 groundNormal;
	private bool wallSliding;
	private bool lastWasWall;
	private float wallNormalX;
	private int groundFrames = 100; // number of frames since player touched ground or wall
	private int jumpFrames = 100; // number of frames since last jump keypress
	
	private float forceMoveTimer = 0f;
	private float forceMoveX;

	private bool dead;

	// inputs:
	private Vector2 input;
	private bool jump;

	// misc:
	private Rigidbody2D rigid;

	private Level level;

	private ContactFilter2D contactFilter;
	private RaycastHit2D[] raycastResults;

	public void Awake() {
		this.rigid = this.gameObject.GetComponent<Rigidbody2D>();

		this.level = GameObject.FindObjectOfType<Level>();

		this.contactFilter = new ContactFilter2D();
		this.contactFilter.SetLayerMask(Physics2D.GetLayerCollisionMask(this.gameObject.layer));
		this.contactFilter.useLayerMask = true;
		this.contactFilter.useTriggers = false;

		this.raycastResults = new RaycastHit2D[32];
	}

	public void Spawn(Vector3 location) {
		this.transform.position = location;

		// reset state:
		this.dead = false;
		this.velocity = Vector2.zero;
		this.velocityXRef = 0;
		this.grounded = false;
		this.groundNormal = Vector2.zero;
		this.wallSliding = false;
		this.lastWasWall = false;
		this.wallNormalX = 0;
		this.groundFrames = 100;
		this.jumpFrames = 100;
		this.forceMoveTimer = 0f;
	}

	public void Update() {
		HandleInput();
	}

	public void OnTriggerEnter2D(Collider2D trigger) {
		if(trigger.tag == "Spikes") {
			Die();
		}
	}

	public void FixedUpdate() {
		if(this.dead) {
			ResetInput();
			this.jumpFrames = 100;
			this.groundFrames = 100;
			this.input = Vector2.zero;
		}

		// handle horizontal movement:

		this.wallSliding = false;

		Vector2 moveVector;

		if(this.grounded) {
			this.velocity.x *= 1f - this.groundFriction;

			if(Mathf.Abs(this.input.x) > 0.1f) {
				this.velocity.x = this.input.x * this.moveSpeed;
			}
			
			moveVector = new Vector2(this.groundNormal.y, -this.groundNormal.x) * this.velocity.x * Time.deltaTime;
		
			this.velocityXRef = 0;
		} else {
			if(Mathf.Abs(this.input.x) > 0.1f) { // air control
				this.velocity.x = Mathf.SmoothDamp(
					this.velocity.x,
					this.moveSpeed * (this.forceMoveTimer > 0 ? this.forceMoveX : this.input.x),
					ref this.velocityXRef,
					this.airAccelerationTime
				);
			}

			moveVector = Vector2.right * this.velocity.x * Time.deltaTime;
		}

		this.forceMoveTimer -= Time.deltaTime;

		float dist = moveVector.magnitude;

		int c = this.rigid.Cast(moveVector, this.contactFilter, this.raycastResults, dist + LOOK_AHEAD_DIST);

		for(int i = 0; i < c; i++) {
			RaycastHit2D hit = this.raycastResults[i];
			Vector2 normal = hit.normal;

			if(Mathf.Abs(normal.y) < MIN_GROUND_NORMAL_Y && !this.grounded) {
				this.wallSliding = true;
				this.lastWasWall = true;
				this.groundFrames = 0;
				this.wallNormalX = normal.x;
	
				normal.y = 0;
			}

			float p = Vector2.Dot(velocity, normal);
			if(p < 0) {
				this.velocity -= p * normal;
			}

			if(hit.distance - LOOK_AHEAD_DIST < dist) {
				dist = hit.distance - LOOK_AHEAD_DIST;
			}
		}

		this.rigid.position += moveVector.normalized * dist;

		// jump:

		if(this.grounded || this.wallSliding) {
			if(this.jump || this.jumpFrames < this.jumpAheadFrames) {
				Jump();
			}
		} else {
			if(this.jump && this.groundFrames < this.coyoteFrames) {
				Jump();
			}
		}

		// handle vertical movement:

		this.grounded = false;

		this.velocity += Physics2D.gravity * Time.deltaTime;

		if(this.wallSliding && this.velocity.y < 0 && Mathf.Sign(input.x) == -Mathf.Sign(this.wallNormalX) && Mathf.Abs(input.x) > 0.1f) {
			this.velocity.y *= this.wallSlideDamping;
		}

		Vector2 deltaY = Vector2.up * this.velocity.y * Time.deltaTime;

		dist = Mathf.Abs(deltaY.y);

		c = this.rigid.Cast(deltaY, this.contactFilter, this.raycastResults, dist + LOOK_AHEAD_DIST);

		for(int i = 0; i < c; i++) {
			RaycastHit2D hit = this.raycastResults[i];
			Vector2 normal = hit.normal;

			if(normal.y > MIN_GROUND_NORMAL_Y) {
				this.grounded = true;
				this.lastWasWall = false;
				this.groundFrames = 0;
				this.groundNormal = normal;

				normal.x = 0;
			}

			float p = Vector2.Dot(velocity, normal);
			if(p < 0) {
				this.velocity -= p * normal;
			}

			if(hit.distance - LOOK_AHEAD_DIST < dist) {
				dist = hit.distance - LOOK_AHEAD_DIST;
			}
		}

		this.rigid.position += deltaY.normalized * dist;

		// misc:

		ResetInput();
		
		this.jumpFrames++;
		this.groundFrames++;
	}

	private void Jump() {
		if(this.lastWasWall) { // wall jump
			this.velocity.x += Mathf.Sign(this.wallNormalX) * Mathf.Cos(this.wallJumpAngle * Mathf.Deg2Rad) * this.jumpSpeed;
			this.velocity.y = Mathf.Sin(this.wallJumpAngle * Mathf.Deg2Rad) * this.jumpSpeed;
		
			this.forceMoveTimer = this.wallJumpDuration;
			this.forceMoveX = Mathf.Sign(this.wallNormalX);
		} else {
			this.velocity.y = this.jumpSpeed;
		}

		this.jumpFrames += 100;
		this.groundFrames += 100;
	}

	private void Die() {
		if(this.dead) {
			return;
		}

		this.dead = true;
		this.level.PlayerDied();
	}

	private void HandleInput() {
		if(Input.GetKeyDown(KeyCode.Space)) {
			this.jump = true;
			
			this.jumpFrames = 0;
		} else {
			this.jump = false;
		}

		this.input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
	}

	private void ResetInput() {
		this.jump = false;
	}
}
