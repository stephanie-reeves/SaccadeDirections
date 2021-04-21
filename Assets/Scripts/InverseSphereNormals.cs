using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//[RequireComponent(typeof(MeshFilter))]

public class InverseSphereNormals : MonoBehaviour
{
	public GameObject SphereObj;

	void Awake()
	{
		InvertSphere();
	}

	void InvertSphere()
	{
		Vector3[] normals = SphereObj.GetComponent<MeshFilter>().mesh.normals;
		for (int i = 0; i < normals.Length; i++)
		{
			normals[i] = -normals[i];
		}
		SphereObj.GetComponent<MeshFilter>().sharedMesh.normals = normals;

		int[] triangles = SphereObj.GetComponent<MeshFilter>().sharedMesh.triangles;
		for (int i = 0; i < triangles.Length; i += 3)
		{
			int t = triangles[i];
			triangles[i] = triangles[i + 2];
			triangles[i + 2] = t;
		}

		SphereObj.GetComponent<MeshFilter>().sharedMesh.triangles = triangles;
	}
}
//void Start()
//{
//	MeshFilter filter = GetComponent(typeof(MeshFilter)) as MeshFilter;
//	if (filter != null)
//	{
//		Mesh mesh = filter.mesh;

//		Vector3[] normals = mesh.normals;
//		for (int i = 0; i < normals.Length; i++)
//			normals[i] = -normals[i];
//		mesh.normals = normals;

//		for (int m = 0; m < mesh.subMeshCount; m++)
//		{
//			int[] triangles = mesh.GetTriangles(m);
//			for (int i = 0; i < triangles.Length; i += 3)
//			{
//				int temp = triangles[i + 0];
//				triangles[i + 0] = triangles[i + 1];
//				triangles[i + 1] = temp;
//			}
//			mesh.SetTriangles(triangles, m);
//		}
//	}

//	Shader "Show Insides"; {
//		SubShader; {

//			Tags { "RenderType" = "Opaque" };

//			Cull Front;


//	}
//	}
//}
//}
