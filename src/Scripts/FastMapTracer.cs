using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Linq;

public class FastMapTracer : MonoBehaviour
{
	[Header("Image Data")]
	[SerializeField] public Texture2D ProvinceIdTex;
	[SerializeField] private Texture2D terrainNormalTex;
	[SerializeField] private Texture2D lookupTex;
	[SerializeField] private RenderTexture distanceTransformTex;
	[SerializeField] private Texture2D colorMapIndirectionTex;
	[SerializeField] private Texture2D displayedColorMapTex;

	[Header("Line Settings")]
	[SerializeField] private float simplifyTolerance = 0.1f;

	[Header("Line Appearance")]
	[SerializeField] private bool createLines = true;
	[SerializeField] private float widthMultiplier = 20f;
	[SerializeField] public Color LineColor = Color.white;
	[SerializeField] private int splineInterpolationPoints = 3;
	[SerializeField] private float cornerRadius = 0.1f;
	[SerializeField] private Texture2D lineTexture;
	[SerializeField] private float textureScale = 1f;
	[SerializeField] private float depthOffset = -1f;
	[SerializeField] private int layer = 1;
	[SerializeField] private int renderOrder = 4000;
	[SerializeField] private float viewOffset = 0f;
	[SerializeField] private float feather = 3f;
	[SerializeField] private bool transparent = false;

	[Header("Provinces")]
	private List<Province> ProvincesInImage;

	[HideInInspector] public Dictionary<int, Biome> BiomeDict;
	[HideInInspector] public Dictionary<Color32, Province> ProvinceColorDict;
	[HideInInspector] public Dictionary<int, Province> ProvinceIntIDDict;
	[HideInInspector] public Dictionary<int, Border> BorderIDDict;
	[HideInInspector] public List<Border> BorderList;

	IEnumerator ComputeAllProvinceBorderPointsCoroutine()
	{
		float timer = Time.realtimeSinceStartup;

		Color32[] pixels = ProvinceIdTex.GetPixels32();

		int width = ProvinceIdTex.width;
		int height = ProvinceIdTex.height;
		int numPixels = width * height;

		bool[] visited = new bool[numPixels];

		Queue<TraceStartIndex> startPointQueue = new Queue<TraceStartIndex>();

		int scanIndex = 0;
		int traceIndex = 0;
		int spinIndex = 4; //starting from left

		bool complete = false;

		int borderCount = 0;

		//scan for first non-black pixel
		while (true)
		{
			if (ColorIDEquals(pixels[traceIndex], Color.black))
			{
				traceIndex++;
				continue;
			}

			break;
		}

		startPointQueue.Enqueue(new TraceStartIndex(traceIndex, spinIndex));

		//OPERATION LOOP
		while (true)
		{
			//TRACE STEP
			while (true)
			{
				//if we've run out of startpoints, terminate the loop.
				if (startPointQueue.Count == 0)
				{
					break;
				}

				TraceStartIndex traceStartIndex = startPointQueue.Dequeue();

				int startingIndex = traceStartIndex.index;
				traceIndex = startingIndex;
				int startingSpin = traceStartIndex.fromDir;
				spinIndex = startingSpin;

				int lastX = -1;
				int lastY = -1;
				int lastSpinIndex = -1;

				//this starting index has already been visited, so skip it
				if (visited[startingIndex] == true)
				{
					continue;
				}

				Color32 currentBordering = Color.white;
				List<float2> points = new List<float2>();
				Border border = null;

				if (!ProvinceColorDict.TryGetValue(pixels[startingIndex], out Province currentProv))
				{
					Debug.LogError("Couldn't find province with Color ID " + pixels[startingIndex].ToString());
					yield break;
				}

				while (true)
				{
					visited[traceIndex] = true;

					int x = traceIndex % width;
					int y = traceIndex / width;

					int spinCount = 0;

					//spin up to 8 times
					while (true)
					{
						if (TryGetNeighborIndex(x, y, 1, (NEIGHBOR_DIR)spinIndex, out int neighborIndex, width, height))
						{
							//this pixel is same color as the traceIndex
							if (ColorIDEquals(pixels[traceIndex], pixels[neighborIndex]))
							{
								//we've found the new trace index for the next loop
								traceIndex = neighborIndex;
								spinIndex = GetPreviousSpinIndexClockwise(spinIndex);
								break;
							}
							//this pixel is the bordering prov color we're currently operating on
							else if (ColorIDEquals(pixels[neighborIndex], currentBordering))
							{
								//AND it's a direct neighbor
								//AND it hasn't been visited
								if (spinIndex % 2 == 0 && (visited[neighborIndex] == false))
								{
									if (points.Count == 0)
									{
										//For the first point of a border, add an extra point in the corner of the pixel.
										float2 p0 = GetPixelBorderCornerUVsFromSpinBehind(spinIndex, x, y, width, height);
										points.Add(p0);
									}

									//add the points
									float2 p = GetPixelBorderUVsFromSpin(spinIndex, x, y, width, height);
									points.Add(p);

									lastX = x;
									lastY = y;
									lastSpinIndex = spinIndex;
								}
							}
							//this pixel is neither our color nor the current operative color and it's a direct neighbor
							else if (spinIndex % 2 == 0)
							{
								//flush the accrued points to the previous operative color province border
								if (border != null && points.Count > 0)
								{
									//Add a last pixel
									float2 pLast = GetPixelBorderCornerUVsFromSpinAhead(lastSpinIndex, lastX, lastY, width, height);
									points.Add(pLast);

									List<Vector2> simplifiedPoints = new List<Vector2>();
									LineUtility.Simplify(points.Select(val => new Vector2(val.x, val.y)).ToList(), simplifyTolerance, simplifiedPoints);
									points = simplifiedPoints.Select(val => new float2(val.x, val.y)).ToList();

									border.Points.Add(points);
									points = new List<float2>();
								}

								//Check to make sure this pixel isn't a lone pixel.
								bool isLonePixel = true;

								for (int i = 0; i < 4; i++)
								{
									//Go through every direction and if this pixel has a direct bordering same colored pixel, it's not alone.
									if (TryGetNeighborIndex(neighborIndex, (NEIGHBOR_DIR)(i * 2), out int potentialStartPointNeighborIndex, width, height))
									{
										if (ColorIDEquals(pixels[neighborIndex], pixels[potentialStartPointNeighborIndex]))
										{
											isLonePixel = false;
										}
									}
								}

								if (isLonePixel == false)
								{
									//make this color the new operative color
									currentBordering = pixels[neighborIndex];

									//try to find this province by color id
									if (ProvinceColorDict.TryGetValue(currentBordering, out Province borderingProv))
									{
										//province exists, get or create a border object
										if (!currentProv.Borders.TryGetValue(borderingProv.ID, out border))
										{
											border = new Border(borderCount++, currentProv.ID, borderingProv.ID);
											currentProv.Borders.Add(borderingProv.ID, border);
											borderingProv.Borders.Add(currentProv.ID, border);

											BorderList.Add(border);
										}

									}
									else
									{
										//province doesn't exist, break
										Debug.LogError("Couldn't province with Color ID " + currentBordering.ToString());
										yield break;
									}

									//enqueue a new starting point
									if (visited[neighborIndex] == false && !(traceIndex == startingIndex && spinIndex != startingSpin) && !ColorIDEquals(pixels[neighborIndex], Color.black))
									{
										TraceStartIndex newStartIndex = new TraceStartIndex(neighborIndex, GetOppositeSpinIndex(spinIndex));
										startPointQueue.Enqueue(newStartIndex);
									}

									//if this is a direct neighbor and it hasn't been visited, add points
									if (spinIndex % 2 == 0 && visited[neighborIndex] == false)
									{
										if (points.Count == 0)
										{
											//For the first point of a border, add an extra point in the corner of the pixel.
											float2 p0 = GetPixelBorderCornerUVsFromSpinBehind(spinIndex, x, y, width, height);
											points.Add(p0);
										}

										float2 p = GetPixelBorderUVsFromSpin(spinIndex, x, y, width, height);
										points.Add(p);

										lastX = x;
										lastY = y;
										lastSpinIndex = spinIndex;
									}
								}
							}
						}
						else
						{
							//hit a boundary
							//flush the accrued points to the previous operative color province border
							if (border != null && points.Count > 0)
							{
								//Add a last point
								float2 pLast = GetPixelBorderCornerUVsFromSpinAhead(lastSpinIndex, lastX, lastY, width, height);
								points.Add(pLast);

								//Simplify the points
								List<Vector2> simplifiedPoints = new List<Vector2>();
								LineUtility.Simplify(points.Select(val => new Vector2(val.x, val.y)).ToList(), simplifyTolerance, simplifiedPoints);
								points = simplifiedPoints.Select(val => new float2(val.x, val.y)).ToList();

								border.Points.Add(points);
								points = new List<float2>();
							}

							currentBordering = Color.white;
						}

						spinIndex++;
						if (spinIndex >= 8)
							spinIndex = 0;
						else if (spinIndex < 0)
							spinIndex = 7;

						spinCount++;
						if (spinCount >= 8)
						{
							//Debug.LogError("Spun all the way around a pixel and found nothing. Index: " + traceIndex + " Color: " + pixels[traceIndex].ToString() + " SpinIndex: " + spinIndex);
							points.Clear();

							//GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
							//go.transform.localScale /= 50;
							//go.transform.position = new Vector3((((float)traceIndex % (float)width) / (float)width) * 100f, 0f, (((float)traceIndex / (float)height) / (float)height) * 100f);
							break;
						}
					}

					if (traceIndex == startingIndex)
					{
						if (border != null && points.Count > 0)
						{
							int traceX = traceIndex % width;
							int traceY = traceIndex / width;

							//Add a last pixel
							float2 pLast = GetPixelBorderCornerUVsFromSpinBehind(startingSpin, traceX, traceY, width, height);
							points.Add(pLast);

							List<Vector2> simplifiedPoints = new List<Vector2>();
							LineUtility.Simplify(points.Select(val => new Vector2(val.x, val.y)).ToList(), simplifyTolerance, simplifiedPoints);
							points = simplifiedPoints.Select(val => new float2(val.x, val.y)).ToList();

							border.Points.Add(points);
							points = new List<float2>();
						}

						break;
					}
				}
			}

			//SCAN STEP
			while (true)
			{
				if (scanIndex >= numPixels)
				{
					complete = true;
					break;
				}

				if (visited[scanIndex] == true)
				{
					scanIndex++;
					continue;
				}

				int scanX = scanIndex % width;
				int scanY = scanIndex / width;

				bool foundNewStartingPoint = false;

				//if current index hasn't been visited,
				//spin direct neighbors to see if we find a new starting point
				for (int i = 0; i < 4; i++)
				{
					int spin = i * 2;

					if (TryGetNeighborIndex(scanX, scanY, 1, (NEIGHBOR_DIR)spin, out int neighborIndex, width, height))
					{
						//found a neighboring pair of unequal colors
						if (!ColorIDEquals(pixels[scanIndex], pixels[neighborIndex]) && visited[neighborIndex] == false && !ColorIDEquals(pixels[neighborIndex], Color.black))
						{
							TraceStartIndex newStartIndex = new TraceStartIndex(neighborIndex, GetOppositeSpinIndex(spin));
							startPointQueue.Enqueue(newStartIndex);

							foundNewStartingPoint = true;
							break;
						}
					}
				}

				scanIndex++;

				if (foundNewStartingPoint)
					break;
			}

			if (complete)
				break;
		}

		//All borders create their polylines.
		foreach (Border b in BorderList)
		{
			b.CreatePolylinesFromCurrentPoints(transparent, feather, renderOrder, depthOffset, mapScale, LineColor);
		}

		timer = Time.realtimeSinceStartup - timer;
		Debug.Log("Border tracing algorithm completed successfully and exited! Time: " + timer + " Border Count: " + borderCount); //
	}

	enum NEIGHBOR_DIR { RIGHT, RIGHTDOWN, DOWN, LEFTDOWN, LEFT, LEFTUP, UP, RIGHTUP }

	bool TryGetNeighborIndex(int index, NEIGHBOR_DIR dir, out int neighborIndex, int width, int height)
	{
		neighborIndex = -1;

		int x = index % width;
		int y = index / width;

		switch (dir)
		{
			case NEIGHBOR_DIR.RIGHTUP:
				if (x + 1 < width && y + 1 < height)
				{
					neighborIndex = (x + 1) + (width * (y + 1));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.RIGHT:
				if (x + 1 < width)
				{
					neighborIndex = (x + 1) + (width * y);
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.RIGHTDOWN:
				if (x + 1 < width && y - 1 >= 0)
				{
					neighborIndex = (x + 1) + (width * (y - 1));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.DOWN:
				if (y - 1 >= 0)
				{
					neighborIndex = x + (width * (y - 1));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.LEFTDOWN:
				if (x - 1 >= 0 && y - 1 >= 0)
				{
					neighborIndex = (x - 1) + (width * (y - 1));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.LEFT:
				if (x - 1 >= 0)
				{
					neighborIndex = (x - 1) + (width * y);
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.LEFTUP:
				if (x - 1 >= 0 && y + 1 < height)
				{
					neighborIndex = (x - 1) + (width * (y + 1));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.UP:
				if (y + 1 < height)
				{
					neighborIndex = x + (width * (y + 1));
					return true;
				}
				else
				{
					return false;
				}
		}

		return false;
	}

	bool TryGetNeighborIndex(int x, int y, int neighborDistance, NEIGHBOR_DIR dir, out int neighborIndex, int width, int height)
	{
		neighborIndex = -1;

		switch (dir)
		{
			case NEIGHBOR_DIR.RIGHTUP:
				if (x + neighborDistance < width && y + neighborDistance < height)
				{
					neighborIndex = (x + neighborDistance) + (width * (y + neighborDistance));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.RIGHT:
				if (x + neighborDistance < width)
				{
					neighborIndex = (x + neighborDistance) + (width * y);
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.RIGHTDOWN:
				if (x + neighborDistance < width && y - neighborDistance >= 0)
				{
					neighborIndex = (x + neighborDistance) + (width * (y - neighborDistance));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.DOWN:
				if (y - neighborDistance >= 0)
				{
					neighborIndex = x + (width * (y - neighborDistance));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.LEFTDOWN:
				if (x - neighborDistance >= 0 && y - neighborDistance >= 0)
				{
					neighborIndex = (x - neighborDistance) + (width * (y - neighborDistance));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.LEFT:
				if (x - neighborDistance >= 0)
				{
					neighborIndex = (x - neighborDistance) + (width * y);
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.LEFTUP:
				if (x - neighborDistance >= 0 && y + neighborDistance < height)
				{
					neighborIndex = (x - neighborDistance) + (width * (y + neighborDistance));
					return true;
				}
				else
				{
					return false;
				}
			case NEIGHBOR_DIR.UP:
				if (y + neighborDistance < height)
				{
					neighborIndex = x + (width * (y + neighborDistance));
					return true;
				}
				else
				{
					return false;
				}
		}

		return false;
	}

	private float2 GetPixelBorderCornerUVsFromSpinBehind(int spinIndex, int x, int y, int width, int height)
	{
		float2 result = float2.zero;

		switch (spinIndex)
		{
			case 0:
				{
					//right -> rightup
					result = new float2((x + 1f) / (float)width, (y + 1f) / (float)height);
					break;
				}
			case 2:
				{
					//down -> rightdown
					result = new float2((x + 1f) / (float)width, y / (float)height);
					break;
				}
			case 4:
				{
					//left -> leftdown
					result = new float2(x / (float)width, y / (float)height);
					break;
				}
			case 6:
				{
					//up -> leftup
					result = new float2(x / (float)width, (y + 1f) / (float)height);
					break;
				}
		}

		return result;
	}

	private float2 GetPixelBorderCornerUVsFromSpinAhead(int spinIndex, int x, int y, int width, int height)
	{
		float2 result = float2.zero;

		switch (spinIndex)
		{
			case 0:
				{
					//right -> rightdown
					result = new float2((x + 1f) / (float)width, y / (float)height);
					break;
				}
			case 2:
				{
					//down -> leftdown
					result = new float2(x / (float)width, y / (float)height);
					break;
				}
			case 4:
				{
					//left -> leftup
					result = new float2(x / (float)width, (y + 1f) / (float)height);
					break;
				}
			case 6:
				{
					//up -> rightup
					result = new float2((x + 1f) / (float)width, (y + 1f) / (float)height);
					break;
				}
		}

		return result;
	}

	private float2 GetPixelBorderUVsFromSpin(int spinIndex, int x, int y, int width, int height)
	{
		float2 result = float2.zero;

		switch (spinIndex)
		{
			case 0:
				{
					//right
					result = new float2((x + 1f) / (float)width, (y + 0.5f) / (float)height);
					break;
				}
			case 2:
				{
					//down
					result = new float2((x + 0.5f) / (float)width, y / (float)height);
					break;
				}
			case 4:
				{
					//left
					result = new float2(x / (float)width, (y + 0.5f) / (float)height);
					break;
				}
			case 6:
				{
					//up
					result = new float2((x + 0.5f) / (float)width, (y + 1f) / (float)height);
					break;
				}
		}

		return result;
	}

	private int GetPreviousSpinIndexClockwise(int inputSpin)
	{
		int result = inputSpin % 2 == 0 ? inputSpin - 2 : inputSpin - 3;

		if (result < 0)
			result += 8;

		return result;
	}

	private int GetOppositeSpinIndex(int inputSpin)
	{
		int result = inputSpin - 4;

		if (result < 0)
		{
			result += 8;
		}

		return result;
	}

	class TraceStartIndex
	{
		public int index;
		public int fromDir;

		public TraceStartIndex(int index, int fromDir)
		{
			this.index = index;
			this.fromDir = fromDir;
		}
	}

	public static bool ColorIDEquals(Color32 a, Color32 b)
	{
		if (a.r == b.r && a.g == b.g && a.b == b.b)
			return true;

		return false;
	}
}
