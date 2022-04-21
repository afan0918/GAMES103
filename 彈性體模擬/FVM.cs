using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;

public class FVM : MonoBehaviour
{
	float dt 		= 0.003f;
	float mass 		= 1;
	float stiffness_0	= 20000.0f;
	float stiffness_1 	= 5000.0f;
	float damp		= 0.999f;

	float g = 9.81f;

	int[] 		Tet;		// 四面體(tetrahedra)的數據(位置)
	int 		tet_number;	// The number of tetrahedra

	Vector3[] 	Force;		// 力
	Vector3[] 	V;		// 速度
	Vector3[] 	X;		// 彈性體節點位置
	int number;			// The number of vertices

	Matrix4x4[] 	inv_Dm;

	// For Laplacian smoothing.
	Vector3[]	V_sum;
	int[]		V_num;

	Vector3 P = new Vector3(0, -3, 0);
	Vector3 N = new Vector3(0, 1, 0);

	SVD svd = new SVD();

	// Start is called before the first frame update
	void Start()
	{
		// FILO IO: Read the house model from files.
		// The model is from Jonathan Schewchuk's Stellar lib.
		{
			string fileContent = File.ReadAllText("Assets/house2.ele");
			string[] Strings = fileContent.Split(new char[] {' ', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);

			tet_number = int.Parse(Strings[0]);
			Tet = new int[tet_number * 4];

			for (int tet = 0; tet < tet_number; tet++)
			{
				Tet[tet * 4 + 0] = int.Parse(Strings[tet * 5 + 4]) - 1;
				Tet[tet * 4 + 1] = int.Parse(Strings[tet * 5 + 5]) - 1;
				Tet[tet * 4 + 2] = int.Parse(Strings[tet * 5 + 6]) - 1;
				Tet[tet * 4 + 3] = int.Parse(Strings[tet * 5 + 7]) - 1;
			}
		}
		{
			string fileContent = File.ReadAllText("Assets/house2.node");
			string[] Strings = fileContent.Split(new char[] {' ', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
			number = int.Parse(Strings[0]);
			X = new Vector3[number];
			for (int i = 0; i < number; i++)
			{
				X[i].x = float.Parse(Strings[i * 5 + 5]) * 0.4f;
				X[i].y = float.Parse(Strings[i * 5 + 6]) * 0.4f;
				X[i].z = float.Parse(Strings[i * 5 + 7]) * 0.4f;
			}
			//Centralize the model.
			Vector3 center = Vector3.zero;
			for (int i = 0; i < number; i++) {
				center += X[i];
			}
			center = center / number;
			for (int i = 0; i < number; i++)
			{
				X[i] -= center;
				float temp = X[i].y;
				X[i].y = X[i].z;
				X[i].z = temp;
			}
		}

		// Create triangle mesh.
		Vector3[] vertices = new Vector3[tet_number * 12];
		int vertex_number = 0;
		for (int tet = 0; tet < tet_number; tet++)
		{
			vertices[vertex_number++] = X[Tet[tet * 4 + 0]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 2]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 1]];

			vertices[vertex_number++] = X[Tet[tet * 4 + 0]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 3]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 2]];

			vertices[vertex_number++] = X[Tet[tet * 4 + 0]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 1]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 3]];

			vertices[vertex_number++] = X[Tet[tet * 4 + 1]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 2]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 3]];
		}

		int[] triangles = new int[tet_number * 12];
		for (int t = 0; t < tet_number * 4; t++)
		{
			triangles[t * 3 + 0] = t * 3 + 0;
			triangles[t * 3 + 1] = t * 3 + 1;
			triangles[t * 3 + 2] = t * 3 + 2;
		}
		Mesh mesh = GetComponent<MeshFilter> ().mesh;
		mesh.vertices  = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals ();


		V 	  = new Vector3[number];
		Force = new Vector3[number];
		V_sum = new Vector3[number];
		V_num = new int[number];

		// allocate and assign inv_Dm
		inv_Dm = new Matrix4x4[tet_number];
		for (int tet = 0; tet < tet_number; tet++)
		{
			inv_Dm[tet] = Build_Edge_Matrix(tet).inverse;
			// Debug.Log("inv_Dm:" + inv_Dm[tet]);
		}
	}

	Matrix4x4 Build_Edge_Matrix(int tet)
	{
		Matrix4x4 ret = Matrix4x4.zero;
		// build edge matrix.

		ret[0, 0] = X[Tet[tet * 4 + 1]][0] - X[Tet[tet * 4]][0];
		ret[1, 0] = X[Tet[tet * 4 + 1]][1] - X[Tet[tet * 4]][1];
		ret[2, 0] = X[Tet[tet * 4 + 1]][2] - X[Tet[tet * 4]][2];

		ret[0, 1] = X[Tet[tet * 4 + 2]][0] - X[Tet[tet * 4]][0];
		ret[1, 1] = X[Tet[tet * 4 + 2]][1] - X[Tet[tet * 4]][1];
		ret[2, 1] = X[Tet[tet * 4 + 2]][2] - X[Tet[tet * 4]][2];

		ret[0, 2] = X[Tet[tet * 4 + 3]][0] - X[Tet[tet * 4]][0];
		ret[1, 2] = X[Tet[tet * 4 + 3]][1] - X[Tet[tet * 4]][1];
		ret[2, 2] = X[Tet[tet * 4 + 3]][2] - X[Tet[tet * 4]][2];

		ret[3, 3] = 1;

		return ret;
	}

	Matrix4x4 M_Multipy(Matrix4x4 m, float scale)
	{
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				m[i, j] *= scale;
			}
		}

		return m;
	}

	Matrix4x4 M_addtion(Matrix4x4 m1, Matrix4x4 m2)
	{
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				m1[i, j] += m2[i, j];
			}
		}

		return m1;
	}

	Matrix4x4 M_Dec(Matrix4x4 m1, Matrix4x4 m2)
	{
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				m1[i, j] -= m2[i, j];
			}
		}

		return m1;
	}

	float un = 0.7f; // F = μN (μ 為滑動摩擦係數，N 為正向力)
	float ut = 0.8f;
	void Update_Collision_Point(int idx, float distance, Vector3 N)
	{
		X[idx] -= distance * N;
		float vDotN = Vector3.Dot(V[idx], N);
		if (vDotN >= 0) return; // 方向一致

		Vector3 v_N = vDotN * N; // 陷進N內部的分量
		Vector3 v_T = V[idx] - v_N; // 沿著面的分量

		// 求摩擦係數，取 max 是保險不讓模型越滑越快
		float frictionCoef = Mathf.Max(1.0f - dt * ut * (1.0f + un) * v_N.magnitude / v_T.magnitude, 0.0f);

		// 計算後更新速度
		Vector3 v_N_new = -un * v_N;
		Vector3 v_T_new = frictionCoef * v_T;
		V[idx] = v_N_new + v_T_new;
	}

	Matrix4x4 StVK(Matrix4x4 f)
	{
		Matrix4x4 p = Matrix4x4.zero;
		Matrix4x4 u = Matrix4x4.zero;
		Matrix4x4 s = Matrix4x4.zero;
		Matrix4x4 v = Matrix4x4.zero;
		Matrix4x4 plamda = Matrix4x4.zero;

		// F = UΛV^T
		svd.svd(f, ref u, ref s, ref v);

		float lambda0 = s[0, 0];
		float lambda1 = s[1, 1];
		float lambda2 = s[2, 2];

		float Ic = lambda0 * lambda0 + lambda1 * lambda1 + lambda2 * lambda2;

		float Ic0 = 2f * lambda0;
		float Ic1 = 2f * lambda1;
		float Ic2 = 2f * lambda2;

		float IIc0 = 4f * lambda0 * lambda0 * lambda0;
		float IIc1 = 4f * lambda1 * lambda1 * lambda1;
		float IIc2 = 4f * lambda2 * lambda2 * lambda2;

		// W = (S0 / 8) * (Ic - 3) ^ 2 + (S1 / 4) * (IIc - 2Ic +3)
		// http://web.cse.ohio-state.edu/~wang.3602/Wang-2016-DME/Wang-2016-DME.pdf
		// 參考論文 5.1 節
		
		plamda[0, 0] = stiffness_0 * (Ic - 3f) * Ic0 / 4f +
		               stiffness_1 * (IIc0 - 2 * Ic0) / 4f;
		plamda[1, 1] = stiffness_0 * (Ic - 3f) * Ic1 / 4f +
		               stiffness_1 * (IIc1 - 2 * Ic1) / 4f;
		plamda[2, 2] = stiffness_0 * (Ic - 3f) * Ic2 / 4f +
		               stiffness_1 * (IIc2 - 2 * Ic2) / 4f;
		plamda[3, 3] = 1f;

		// P = Udiag(𝜕𝑊/𝜕𝜆0,𝜕𝑊/𝜕𝜆1,𝜕𝑊/𝜕𝜆2)V^T
		p = u * plamda * v.transpose;
		return p;
	}

	void _Update()
	{
		// Jump up.
		if (Input.GetKeyDown(KeyCode.W))
		{
			for (int i = 0; i < number; i++)
				V[i].y += 5f;
		}
		if (Input.GetKeyDown(KeyCode.S))
		{
			for (int i = 0; i < number; i++)
				V[i].y -= 5f;
		}
		// 向右(展示地板摩擦力用)
		if (Input.GetKeyDown(KeyCode.D))
		{
			for (int i = 0; i < number; i++)
				V[i].x += 1f;
		}
		// 向左(展示地板摩擦力用)
		if (Input.GetKeyDown(KeyCode.A))
		{
			for (int i = 0; i < number; i++)
				V[i].x -= 1f;
		}

		for (int i = 0 ; i < number; i++)
		{
			// Add gravity to Force.
			Force[i] = new Vector3(0, -g, 0);
		}

		for (int tet = 0; tet < tet_number; tet++)
		{
			// 單位矩陣
			Matrix4x4 I = Matrix4x4.identity;

			// Deformation Gradient
			Matrix4x4 F = Build_Edge_Matrix(tet) * inv_Dm[tet];

			// Green Strain
			// G = (1/2)(FTrans*F-I) = (1/2)(VD^2VTrans-I)
			Matrix4x4 G = M_Dec(F.transpose * F, I);

			// Second PK Stress
			// W 對 G 偏微分 = 2μG + λ*trace(G)I = S
			float trace = G[0, 0] + G[1, 1] + G[2, 2] + G[3, 3];
			Matrix4x4 S = M_addtion(M_Multipy(G, stiffness_0 * 2f), M_Multipy(I, trace * stiffness_1));
			// Matrix4x4 P = F * S;

			// stvk
			Matrix4x4 P = StVK(F);

			// Neo-Hookean 方法(未完成)

			// Elastic Force
			Matrix4x4 force = M_Multipy(P * (inv_Dm[tet].transpose) , -1f / (6f * inv_Dm[tet].determinant));

			Vector3 f1 = new Vector3(force[0, 0], force[1, 0], force[2, 0]);
			Vector3 f2 = new Vector3(force[0, 1], force[1, 1], force[2, 1]);
			Vector3 f3 = new Vector3(force[0, 2], force[1, 2], force[2, 2]);
			Vector3 f0 = -f1 - f2 - f3;

			Force[Tet[4 * tet + 1]] += f1;
			Force[Tet[4 * tet + 2]] += f2;
			Force[Tet[4 * tet + 3]] += f3;
			Force[Tet[4 * tet]] += f0;
		}

		for (int i = 0; i < number; i++)
		{
			//TODO: Update X and V here.
			V[i] = damp * (V[i] + Force[i] / mass * dt);
			X[i] = X[i] + V[i] * dt;

			//TODO: (Particle) collision with floor.
			float distance = Vector3.Dot(X[i] - P, N);
			if (distance < 0)
			{
				Update_Collision_Point(i, distance, N);
			}
			V_sum[i] = Vector3.zero;
			V_num[i] = 0;
		}

		// 笑爛原來是忘記做速度平滑(laplacian)
		// 乾為什麼做了反而模型直接爆掉，完蛋了，後續再檢查原因
		// 原因是速度平滑不能全部採用大家的速度，還要乘上一定比例自己的速度，笑爛，好氣喔
		for (int tet = 0; tet < tet_number; tet++) {
			V_sum[Tet[4 * tet]] += V[Tet[4 * tet + 1]] + V[Tet[4 * tet + 2]] + V[Tet[4 * tet + 3]];
			V_num[Tet[4 * tet]] += 3;
			V_sum[Tet[4 * tet + 1]] += V[Tet[4 * tet]] + V[Tet[4 * tet + 2]] + V[Tet[4 * tet + 3]];
			V_num[Tet[4 * tet + 1]] += 3;
			V_sum[Tet[4 * tet + 2]] += V[Tet[4 * tet + 1]] + V[Tet[4 * tet]] + V[Tet[4 * tet + 3]];
			V_num[Tet[4 * tet + 2]] += 3;
			V_sum[Tet[4 * tet + 3]] += V[Tet[4 * tet + 1]] + V[Tet[4 * tet + 2]] + V[Tet[4 * tet]];
			V_num[Tet[4 * tet + 3]] += 3;
		}

		for (int i = 0; i < number; i++) {
			V[i] = V_sum[i] / V_num[i] * 0.4f + V[i] * 0.6f;
			// Debug.Log("V:" + V[i]);
		}
	}

	// Update is called once per frame
	void Update()
	{
		for (int l = 0; l < 1; l++)
			_Update();

		// Dump the vertex array for rendering.
		Vector3[] vertices = new Vector3[tet_number * 12];
		int vertex_number = 0;
		for (int tet = 0; tet < tet_number; tet++)
		{
			vertices[vertex_number++] = X[Tet[tet * 4 + 0]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 2]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 1]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 0]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 3]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 2]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 0]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 1]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 3]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 1]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 2]];
			vertices[vertex_number++] = X[Tet[tet * 4 + 3]];
		}
		Mesh mesh = GetComponent<MeshFilter> ().mesh;
		mesh.vertices  = vertices;
		mesh.RecalculateNormals ();
	}
}
