using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using Linefy;

[System.Serializable]
public class SerializableBorder
{
	public int ID;
	public int ProvIdA;
	public int ProvIdB;
	public List<List<float3>> PolylineData;

	public SerializableBorder(int id, int provIdA, int provIdB, List<List<float3>> polylineData)
	{
		ID = id;
		ProvIdA = provIdA;
		ProvIdB = provIdB;
		PolylineData = polylineData;
	}
}

[System.Serializable]
public class Border
{
    public int ID;
    public List<List<float2>> Points; //each list is an individual polyline
	public List<Polyline> Polylines;
    public int ProvIdA;
    public int ProvIdB;
	public int PointCount = 0;

	[System.NonSerialized]
	public Color myColor = new Color32(20, 20, 20, 255);
	public float myWidth = 1f;

    public Border(int id, int provIdA, int provIdB)
    {
		this.ID = id;
        this.ProvIdA = provIdA;
        this.ProvIdB = provIdB;

        Points = new List<List<float2>>();
		Polylines = new List<Polyline>();
    }

    public void ComputePointOrder()
    {
        if (Points == null || Points.Count == 0)
        {
            Debug.Log("Border " + ID + " point list was null or empty.");
            return;
        }
    }

	public void SetLineWidth(float width)
	{
		myWidth = width;
	}

	public void SetLineColor(Color color)
	{
		for (int i = 0; i < Polylines.Count; i++)
		{
			Polylines[i].colorMultiplier = color;
		}
	}

	public void DrawPolylines(int layer, float baseWidth)
	{
		for (int i = 0; i < Polylines.Count; i++)
		{
			Polylines[i].widthMultiplier = baseWidth * myWidth;
			Polylines[i].Draw();
		}
	}

	public void CreatePolylinesFromFloat3List(List<List<float3>> pointsList, bool transparent, float feather, int renderOrder, float depthOffset, float mapScale, Color color)
	{
		Points.Clear();

		for (int j = 0; j < pointsList.Count; j++)
		{
			Points.Add(new List<float2>(pointsList[j].Count));

			Polyline p = new Polyline(pointsList[j].Count);
			p.transparent = transparent;
			p.feather = feather;
			p.renderOrder = renderOrder;
			p.depthOffset = depthOffset;
			p.colorMultiplier = color;

			for (int i = 0; i < pointsList[j].Count; i++)
			{
				Vector3 vecA = new Vector3(pointsList[j][i].x * mapScale, pointsList[j][i].y * mapScale, pointsList[j][i].z * mapScale);
				p[i] = new PolylineVertex(vecA, Color.white, 1f);
				Points[j].Add(new float2(pointsList[j][i].x * mapScale, pointsList[j][i].z * mapScale));
				PointCount++;
			}

			Polylines.Add(p);
		}
	}

    public void CreatePolylinesFromCurrentPoints(bool transparent, float feather, int renderOrder, float depthOffset, float mapScale, Color color)
    {
        foreach (List<float2> polyline in Points)
        {
            //NO INTERPOLATION
            Polyline p = new Polyline(polyline.Count);
            p.transparent = transparent;
            p.feather = feather;
            p.renderOrder = renderOrder;
            p.depthOffset = depthOffset;
			p.colorMultiplier = color;

            for (int i = 0; i < polyline.Count; i++)
            {
                Vector3 vecA = new Vector3(polyline[i].x * mapScale, 0f, polyline[i].y * mapScale);
                p[i] = new PolylineVertex(vecA, Color.white, 1f);
				PointCount++;
            }

            Polylines.Add(p);

			#region interp
			//WITH CATMULL ROM INTERPOLATION
			/*if (polyline.Count >= 4)
			{
				Polyline p = new Polyline(2 + (polyline.Count - 3) * splineInterpolationPoints);
				p.transparent = transparent;
				p.feather = feather;
				p.renderOrder = renderOrder;
				p.depthOffset = depthOffset;

				//Create first point directly.
				Vector3 startPoint = new Vector3(polyline[0].x * mapScale, 0f, polyline[0].y * mapScale);
				p[0] = new PolylineVertex(startPoint, Color.white, 1f);

				for (int i = 0; i < polyline.Count - 3; i++)
				{
					for (int j = 0; j < splineInterpolationPoints; j++)
					{
						float2 point = PointOnCatmullRom(polyline[i], polyline[i + 1], polyline[i + 2], polyline[i + 3], (float)j / (float)splineInterpolationPoints);
						Vector3 v = new Vector3(point.x * mapScale, 0f, point.y * mapScale);

						p[1 + (i * splineInterpolationPoints) + j] = new PolylineVertex(v, Color.white, 1f);
					}
				}

				Vector3 endPoint = new Vector3(polyline[polyline.Count - 1].x * mapScale, 0f, polyline[polyline.Count - 1].y * mapScale);
				p[p.count - 1] = new PolylineVertex(endPoint, Color.white, 1f);

				polylines.Add(p);
			}
			else
			{
				//Not enough vertex for corner rounding, so just use direct verts.
				Polyline p = new Polyline(polyline.Count);
				p.transparent = transparent;
				p.feather = feather;
				p.renderOrder = renderOrder;
				p.depthOffset = depthOffset;

				for (int i = 0; i < polyline.Count; i++)
				{
					Vector3 vecA = new Vector3(polyline[i].x * mapScale, 0.0001f, polyline[i].y * mapScale);
					p[i] = new PolylineVertex(vecA, Color.white, 1f);
					pointCount++;
				}

				polylines.Add(p);
			}*/

			//WITH HERMITE INTERPOLATION
			/*if (polyline.Count >= 4)
			{
				List<Vector2> tempPointList = new List<Vector2>(((polyline.Count - 3) * splineInterpolationPoints) + 2);

				//add start point
				tempPointList.Add(new Vector2(polyline[0].x, polyline[0].y));

				for (int i = 0; i < polyline.Count - 3; i++)
				{
					for (int j = 0; j < splineInterpolationPoints; j++)
					{
						float hermX = Linefy.Utilites.HermiteValue(polyline[i].x, polyline[i + 1].x, polyline[i + 2].x, polyline[i + 3].x, (float)j / (float)splineInterpolationPoints);
						float hermY = Linefy.Utilites.HermiteValue(polyline[i].y, polyline[i + 1].y, polyline[i + 2].y, polyline[i + 3].y, (float)j / (float)splineInterpolationPoints);

						tempPointList.Add(new Vector2(hermX, hermY));
					}
				}

				tempPointList.Add(new Vector2(polyline[polyline.Count - 1].x, polyline[polyline.Count - 1].y));

				Polyline p = new Polyline(tempPointList.Count);
				p.transparent = transparent;
				p.feather = feather;
				p.renderOrder = renderOrder;
				p.depthOffset = depthOffset;

				for (int i = 0; i < tempPointList.Count; i++)
				{
					Vector3 v3 = new Vector3(tempPointList[i].x * mapScale, 0f, tempPointList[i].y * mapScale);
					p[i] = new PolylineVertex(v3, Color.white, 1f);
					pointCount++;
				}

				polylines.Add(p);
			}
			else
			{
				Polyline p = new Polyline(polyline.Count);
				p.transparent = transparent;
				p.feather = feather;
				p.renderOrder = renderOrder;
				p.depthOffset = depthOffset;

				for (int i = 0; i < polyline.Count; i++)
				{
					Vector3 vecA = new Vector3(polyline[i].x * 100f, 0.0001f, polyline[i].y * 100f);
					p[i] = new PolylineVertex(vecA, Color.white, 1f);
					pointCount++;
				}

				polylines.Add(p);
			}*/
			#endregion
		}
	}
}

