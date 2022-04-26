using UnityEngine;
using System.Collections;

public class wave_motion : MonoBehaviour
{
	int size 		= 100;
	float rate 		= 0.005f;
	float gamma		= 0.004f;
	float damping 		= 0.996f;
	float[,] 	old_h;
	float[,]	low_h;
	float[,]	vh;
	float[,]	b;

	bool [,]	cg_mask;
	float[,]	cg_p;
	float[,]	cg_r;
	float[,]	cg_Ap;
	bool 		tag = true;

	Vector3 	cube_v = Vector3.zero;
	Vector3 	cube_w = Vector3.zero;

	// Use this for initialization
	void Start ()
	{
		Mesh mesh = GetComponent<MeshFilter> ().mesh;
		mesh.Clear ();

		Vector3[] X = new Vector3[size * size];

		for (int i = 0; i < size; i++)
			for (int j = 0; j < size; j++)
			{
				X[i * size + j].x = i * 0.1f - size * 0.05f;
				X[i * size + j].y = 0;
				X[i * size + j].z = j * 0.1f - size * 0.05f;
			}

		int[] T = new int[(size - 1) * (size - 1) * 6];
		int index = 0;
		for (int i = 0; i < size - 1; i++)
			for (int j = 0; j < size - 1; j++)
			{
				T[index * 6 + 0] = (i + 0) * size + (j + 0);
				T[index * 6 + 1] = (i + 0) * size + (j + 1);
				T[index * 6 + 2] = (i + 1) * size + (j + 1);
				T[index * 6 + 3] = (i + 0) * size + (j + 0);
				T[index * 6 + 4] = (i + 1) * size + (j + 1);
				T[index * 6 + 5] = (i + 1) * size + (j + 0);
				index++;
			}
		mesh.vertices  = X;
		mesh.triangles = T;
		mesh.RecalculateNormals ();

		low_h 	= new float[size, size];
		old_h 	= new float[size, size];
		vh 	  	= new float[size, size];
		b 	  	= new float[size, size];

		cg_mask	= new bool [size, size];
		cg_p 	= new float[size, size];
		cg_r 	= new float[size, size];
		cg_Ap 	= new float[size, size];

		for (int i = 0; i < size; i++)
			for (int j = 0; j < size; j++)
			{
				low_h[i, j] = 99999;
				old_h[i, j] = 0;
				vh[i, j] = 0;
			}
	}

	void A_Times(bool[,] mask, float[,] x, float[,] Ax, int li, int ui, int lj, int uj)
	{
		for (int i = li; i <= ui; i++)
			for (int j = lj; j <= uj; j++)
				if (i >= 0 && j >= 0 && i < size && j < size && mask[i, j])
				{
					Ax[i, j] = 0;
					if (i != 0)		Ax[i, j] -= x[i - 1, j] - x[i, j];
					if (i != size - 1)	Ax[i, j] -= x[i + 1, j] - x[i, j];
					if (j != 0)		Ax[i, j] -= x[i, j - 1] - x[i, j];
					if (j != size - 1)	Ax[i, j] -= x[i, j + 1] - x[i, j];
				}
	}

	float Dot(bool[,] mask, float[,] x, float[,] y, int li, int ui, int lj, int uj)
	{
		float ret = 0;
		for (int i = li; i <= ui; i++)
			for (int j = lj; j <= uj; j++)
				if (i >= 0 && j >= 0 && i < size && j < size && mask[i, j])
				{
					ret += x[i, j] * y[i, j];
				}
		return ret;
	}

	void Conjugate_Gradient(bool[,] mask, float[,] b, float[,] x, int li, int ui, int lj, int uj)
	{
		//Solve the Laplacian problem by CG.
		A_Times(mask, x, cg_r, li, ui, lj, uj);

		for (int i = li; i <= ui; i++)
			for (int j = lj; j <= uj; j++)
				if (i >= 0 && j >= 0 && i < size && j < size && mask[i, j])
				{
					cg_p[i, j] = cg_r[i, j] = b[i, j] - cg_r[i, j];
				}

		float rk_norm = Dot(mask, cg_r, cg_r, li, ui, lj, uj);

		for (int k = 0; k < 128; k++)
		{
			if (rk_norm < 1e-10f)	break;
			A_Times(mask, cg_p, cg_Ap, li, ui, lj, uj);
			float alpha = rk_norm / Dot(mask, cg_p, cg_Ap, li, ui, lj, uj);

			for (int i = li; i <= ui; i++)
				for (int j = lj; j <= uj; j++)
					if (i >= 0 && j >= 0 && i < size && j < size && mask[i, j])
					{
						x[i, j]   += alpha * cg_p[i, j];
						cg_r[i, j] -= alpha * cg_Ap[i, j];
					}

			float _rk_norm = Dot(mask, cg_r, cg_r, li, ui, lj, uj);
			float beta = _rk_norm / rk_norm;
			rk_norm = _rk_norm;

			for (int i = li; i <= ui; i++)
				for (int j = lj; j <= uj; j++)
					if (i >= 0 && j >= 0 && i < size && j < size && mask[i, j])
					{
						cg_p[i, j] = cg_r[i, j] + beta * cg_p[i, j];
					}
		}
	}

	void get_vh(float[,] new_h, string obj)
	{
		GameObject block = GameObject.Find(obj);
		Vector3 p = block.transform.position;

		Mesh mesh = GetComponent<MeshFilter>().mesh;
		Vector3[] X = mesh.vertices;

		// 設定求解範圍以進行優化
		int li = (int)Mathf.Ceil((p.x - 0.5f + 5f) / 0.1f);
		int lj = (int)Mathf.Ceil((p.z - 0.5f + 5f) / 0.1f);
		int ui = (int)Mathf.Floor((p.x + 0.5f + 5f) / 0.1f);
		int uj = (int)Mathf.Floor((p.z + 0.5f + 5f) / 0.1f);

		// calculate low_h;
		for (int i = li; i <= ui; i++)
		{
			for (int j = lj; j <= uj; j++)
			{
				if (i >= 0 && j >= 0 && i < size && j < size)
				{
					cg_mask[i, j] = true;// 遮罩，判定是否需要計算
					low_h[i, j] = 0f;
				}
				else
				{
					cg_mask[i, j] = false;
					low_h[i, j] = new_h[i, j];
				}
			}
		}

		// then set up b and cg_mask for conjugate gradient.
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				b[i, j] = (new_h[i, j] - low_h[i, j]) / rate;
			}
		}
		// Solve the Poisson equation to obtain vh (virtual height).
		Conjugate_Gradient(cg_mask, b, vh, li, ui, lj, uj);
	}

	void Shallow_Wave(float[,] old_h, float[,] h, float [,] new_h)
	{
		// Compute new_h based on the shallow wave model.
		for (int i = 0; i < size; i++) {
			for (int j = 0; j < size; j++) {
				float hij_left = i > 0 ? h[i - 1, j] : h[i, j];
				float hij_right = i + 1 < size ? h[i + 1, j] : h[i, j];
				float hij_top = j + 1 < size ? h[i, j + 1] : h[i, j];
				float hij_botten = j > 0 ? h[i, j - 1] : h[i, j];
				// h(new)i,j ← hi,j + (hi,j − h(old)i,j ) ∗ damping + (hi−1,j + hi+1,j + hi,j−1 + hi,j+1 − 4hi,j ) ∗ rate.
				new_h[i, j] = h[i, j] + (h[i, j] - old_h[i, j]) * damping +
				              rate * (hij_left + hij_right + hij_top + hij_botten - 4 * h[i, j]);
			}
		}

		// Block->Water coupling
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				// 將箱子形成的虛擬水面高度設置為 0
				vh[i, j] = 0;
			}
		}

		get_vh(new_h, "Cube");
		get_vh(new_h, "Block");

		// Diminish vh.
		// v ← γv
		for(int i=0;i<size;i++){
			for(int j=0;j<size;j++){
				vh[i,j] *= gamma;
			}
		}

		// Update new_h by vh.
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				if (i >= 0 && j >= 0 && i < size && j < size)
				{
					if (i != 0) new_h[i, j] += (vh[i - 1, j] - vh[i, j]);
					if (i != size - 1) new_h[i, j] += (vh[i + 1, j] - vh[i, j]);
					if (j != 0) new_h[i, j] += (vh[i, j - 1] - vh[i, j]);
					if (j != size - 1) new_h[i, j] += (vh[i, j + 1] - vh[i, j]);
				}
			}
		}

		// old_h <- h; h <- new_h;
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				old_h[i, j] = h[i, j];
				h[i, j] = new_h[i, j];
			}
		}

		// Water->Block coupling.
		// 未完成
	}


	// Update is called once per frame
	void Update ()
	{
		Mesh mesh = GetComponent<MeshFilter> ().mesh;
		Vector3[] X    = mesh.vertices;
		float[,] new_h = new float[size, size];
		float[,] h     = new float[size, size];

		// Load X.y into h.
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				h[i, j] = X[i * size + j].y;
			}
		}

		if (Input.GetKeyDown ("r"))
		{
			// Add random water.
			for (int i = 0; i < size / 10; i++) {
				for (int j = 0; j < size / 10; j++) {
					h[(int)(size * Random.value), (int)(size * Random.value)] += Random.value / 20;
				}
			}
		}

		for (int l = 0; l < 8; l++)
		{
			Shallow_Wave(old_h, h, new_h);
		}

		// Store h back into X.y and recalculate normal.
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				X[i * size + j].y = h[i, j];
			}
		}

		mesh.vertices = X;
		mesh.RecalculateNormals();
	}
}
