﻿using System;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// A player object that moves automatically in a maze.
/// </summary>
public class Player : MonoBehaviour
{
    /// Base movement speed of the player.
	public float movementSpeed = 0.0f;
	public float turnDistance = 1.0f;

	[SerializeField]
    private Vector3 target;
	[SerializeField]
	private Vector3 nextTarget;

	[SerializeField]
	private Dir _facing;
	public Dir facing { get { return _facing; } set { _facing = nextFacing = value; } }
	[SerializeField]
	private Dir nextFacing;

	private const float TURN_ARC = Mathf.PI / 2.0f;
	[SerializeField]
	private float remainingDist = 0.0f;
	[SerializeField]
	private float remainingTurnDist = 0.0f;

	[HideInInspector]
	public bool canMove = false;

	private enum State
	{
		Rotating,
		Moving
	}
	private State state = State.Rotating;

	[HideInInspector]
	public Maze maze = null;

	void Start()
	{
		Reset();
	}

	void Update()
	{
		if (canMove)
		{
			// Get a new target if the current one has already been reached.
			if (transform.position == target)
				NewTarget();
			
			// Keep moving until the correct distance has been covered.
			float toMove = movementSpeed * Time.deltaTime;
			do
				Move(ref toMove);
			while (toMove > 0.0f);
			
			// Get a new target once all the forwards moving and turning have been done.
			if (remainingDist <= 0.0f && remainingTurnDist <= 0.0f)
				NewTarget();
		}
	}

	private void Move(ref float movementAmount)
	{
		if (remainingDist > 0.0f)	// Keep moving forwards.
		{
			Vector3 towardsTarget = target - transform.position;
			float actualMovement = Mathf.Min(movementAmount, towardsTarget.magnitude);

			// Move forwards.
			if (actualMovement < movementAmount)	// Snap to target if it's closer than the distance we're trying to move.
				transform.position = target;
			else
				transform.Translate(towardsTarget.normalized * movementAmount, Space.World);
			
			movementAmount -= actualMovement;
			remainingDist -= actualMovement;
		}
		else if (remainingTurnDist > 0.0f)	// Keep turning.
		{
			float turnLength = TURN_ARC * turnDistance;
			float actualMovement = Mathf.Min(movementAmount, remainingTurnDist);
			remainingTurnDist -= actualMovement;

			// Calculate position in the 90 degree turn.
			float turnPhase = (turnLength - remainingTurnDist) / turnLength;
			float angle = turnPhase * (Mathf.PI / 2.0f);

			// Calculate movement along the turn.
			float forwards = Mathf.Sin(angle) * turnDistance;
			float sidewards = 1.0f - Mathf.Cos(angle) * turnDistance;
			float xMovement = Utils.NonZero(Nav.DX[facing] * forwards, Nav.DX[nextFacing] * sidewards);
			float yMovement = Utils.NonZero(Nav.DY[facing] * forwards, Nav.DY[nextFacing] * sidewards);
			Vector3 turnDelta = new Vector3(yMovement, 0.0f, xMovement);

			// Adjust the position for the turn.
			Vector3 turnAdjust = new Vector3(Nav.DY[facing] * turnDistance, 0.0f, Nav.DX[facing] * turnDistance);

			// Update position on the turn.
			transform.position = target + turnDelta - turnAdjust;

			// Update rotation.
			int turnDir = 0;
			if (nextFacing == Nav.left[facing])
				turnDir = -1;
			else if (nextFacing == Nav.right[facing])
				turnDir = 1;
			transform.rotation = Quaternion.Euler(0.0f, Nav.FacingToAngle(facing) + 90.0f + (angle * Mathf.Rad2Deg) * turnDir, 0.0f);

			movementAmount -= actualMovement;
		}
		else	// No distance to move anymore, zero out the movement to fix the error.
		{
			Debug.LogWarning("No remaining distance to move or turn but still trying to move for " + movementAmount);
			movementAmount = 0.0f;
		}
	}

    /// <summary>
    /// Set a new target world position to move towards.
    /// </summary>
	private void NewTarget()
	{
		// 
		target = nextTarget;
		facing = nextFacing;

		nextTarget = maze.RoomToWorldPosition(maze.MoveLeftmost(maze.WorldToRoomPosition(target), facing, out nextFacing));

		Vector3 targetDelta = transform.position - target;
		if (facing != nextFacing)
		{
			remainingDist += targetDelta.magnitude - turnDistance;
			remainingTurnDist = TURN_ARC * turnDistance;
		}
		else
		{
			remainingDist += targetDelta.magnitude;
		}
	}

	public void Reset()
	{
        // Reset the player.
		state = State.Rotating;
		target = new Vector3();
		nextTarget = new Vector3();
		NewTarget();
	}
}
