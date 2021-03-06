﻿using UnityEngine;

/// <summary>
/// Billboards the GameObject this script is attached to, making it face the camera at all times (using only Y rotation).
/// </summary>
public class Billboard : MonoBehaviour
{
	void Update()
	{
		if (Camera.main == null)
			return;
		
		Quaternion towardsCamera = Quaternion.LookRotation(transform.position - Camera.main.transform.position, Vector3.up);
		Vector3 lookEulers = towardsCamera.eulerAngles;
		lookEulers.x = lookEulers.z = 0.0f;
		transform.rotation = Quaternion.Euler(lookEulers);
	}
}
