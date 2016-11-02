﻿using UnityEngine;
using System.Collections.Generic;

public enum Dir { N, S, E, W };

class Nav
{
	public static Dictionary<Dir, int> DX = new Dictionary<Dir, int>()
	{ { Dir.N, 0 }, { Dir.S, 0 }, { Dir.W, -1 }, { Dir.E, 1 } };
	public static Dictionary<Dir, int> DY = new Dictionary<Dir, int>()
	{ { Dir.N, -1 }, { Dir.S, 1 }, { Dir.W, 0 }, { Dir.E, 0 } };

	public static Dictionary<Dir, Dir> left = new Dictionary<Dir, Dir>()
	{ { Dir.N, Dir.W }, { Dir.E, Dir.N }, { Dir.S, Dir.E }, { Dir.W, Dir.S } };
	public static Dictionary<Dir, Dir> right = new Dictionary<Dir, Dir>()
	{ { Dir.N, Dir.E }, { Dir.E, Dir.S }, { Dir.S, Dir.W }, { Dir.W, Dir.N } };
	public static Dictionary<Dir, Dir> opposite = new Dictionary<Dir, Dir>()
	{ { Dir.N, Dir.S }, { Dir.E, Dir.W }, { Dir.S, Dir.N }, { Dir.W, Dir.E } };

	public static Dir GetFacing(float rotation)
	{
		if (rotation < 0.0f)
			rotation += 360.0f;
		rotation /= 90.0f;

		int dir = Mathf.RoundToInt(rotation);
		switch (dir)
		{
			case 0:
				return Dir.W;
			case 1:
				return Dir.N;
			case 2:
				return Dir.E;
			case 3:
				return Dir.S;
			default:
				Debug.Log("What the heck is dir " + dir);
				break;
		}

		return Dir.N;
	}
}