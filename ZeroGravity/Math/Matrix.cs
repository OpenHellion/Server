using System;
using System.Text.RegularExpressions;

namespace ZeroGravity.Math;

public class Matrix
{
	public int rows;

	public int cols;

	public double[,] mat;

	public Matrix L;

	public Matrix U;

	private int[] pi;

	private double detOfP = 1.0;

	public double this[int iRow, int iCol]
	{
		get
		{
			return mat[iRow, iCol];
		}
		set
		{
			mat[iRow, iCol] = value;
		}
	}

	public Matrix(int iRows, int iCols)
	{
		rows = iRows;
		cols = iCols;
		mat = new double[rows, cols];
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				mat[i, j] = 0.0;
			}
		}
	}

	public bool IsSquare()
	{
		return rows == cols;
	}

	public Matrix GetCol(int k)
	{
		Matrix j = new Matrix(rows, 1);
		for (int i = 0; i < rows; i++)
		{
			j[i, 0] = mat[i, k];
		}
		return j;
	}

	public void SetCol(Matrix v, int k)
	{
		for (int i = 0; i < rows; i++)
		{
			mat[i, k] = v[i, 0];
		}
	}

	public void MakeLU()
	{
		if (!IsSquare())
		{
			throw new MException("The matrix is not square!");
		}
		L = IdentityMatrix(rows, cols);
		U = Duplicate();
		pi = new int[rows];
		for (int m = 0; m < rows; m++)
		{
			pi[m] = m;
		}
		double p = 0.0;
		int k3 = 0;
		int pom1 = 0;
		for (int k2 = 0; k2 < cols - 1; k2++)
		{
			p = 0.0;
			for (int l = k2; l < rows; l++)
			{
				if (System.Math.Abs(U[l, k2]) > p)
				{
					p = System.Math.Abs(U[l, k2]);
					k3 = l;
				}
			}
			if (p == 0.0)
			{
				throw new MException("The matrix is singular!");
			}
			pom1 = pi[k2];
			pi[k2] = pi[k3];
			pi[k3] = pom1;
			for (int k = 0; k < k2; k++)
			{
				double pom2 = L[k2, k];
				L[k2, k] = L[k3, k];
				L[k3, k] = pom2;
			}
			if (k2 != k3)
			{
				detOfP *= -1.0;
			}
			for (int j = 0; j < cols; j++)
			{
				double pom2 = U[k2, j];
				U[k2, j] = U[k3, j];
				U[k3, j] = pom2;
			}
			for (int i = k2 + 1; i < rows; i++)
			{
				L[i, k2] = U[i, k2] / U[k2, k2];
				for (int n = k2; n < cols; n++)
				{
					U[i, n] -= L[i, k2] * U[k2, n];
				}
			}
		}
	}

	public Matrix SolveWith(Matrix v)
	{
		if (rows != cols)
		{
			throw new MException("The matrix is not square!");
		}
		if (rows != v.rows)
		{
			throw new MException("Wrong number of results in solution vector!");
		}
		if (L == null)
		{
			MakeLU();
		}
		Matrix b = new Matrix(rows, 1);
		for (int i = 0; i < rows; i++)
		{
			b[i, 0] = v[pi[i], 0];
		}
		Matrix z = SubsForth(L, b);
		return SubsBack(U, z);
	}

	public Matrix Invert()
	{
		if (L == null)
		{
			MakeLU();
		}
		Matrix inv = new Matrix(rows, cols);
		for (int i = 0; i < rows; i++)
		{
			Matrix Ei = ZeroMatrix(rows, 1);
			Ei[i, 0] = 1.0;
			Matrix col = SolveWith(Ei);
			inv.SetCol(col, i);
		}
		return inv;
	}

	public double Det()
	{
		if (L == null)
		{
			MakeLU();
		}
		double det = detOfP;
		for (int i = 0; i < rows; i++)
		{
			det *= U[i, i];
		}
		return det;
	}

	public Matrix GetP()
	{
		if (L == null)
		{
			MakeLU();
		}
		Matrix matrix = ZeroMatrix(rows, cols);
		for (int i = 0; i < rows; i++)
		{
			matrix[pi[i], i] = 1.0;
		}
		return matrix;
	}

	public Matrix Duplicate()
	{
		Matrix matrix = new Matrix(rows, cols);
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				matrix[i, j] = mat[i, j];
			}
		}
		return matrix;
	}

	public static Matrix SubsForth(Matrix A, Matrix b)
	{
		if (A.L == null)
		{
			A.MakeLU();
		}
		int k = A.rows;
		Matrix x = new Matrix(k, 1);
		for (int i = 0; i < k; i++)
		{
			x[i, 0] = b[i, 0];
			for (int j = 0; j < i; j++)
			{
				x[i, 0] -= A[i, j] * x[j, 0];
			}
			x[i, 0] /= A[i, i];
		}
		return x;
	}

	public static Matrix SubsBack(Matrix A, Matrix b)
	{
		if (A.L == null)
		{
			A.MakeLU();
		}
		int k = A.rows;
		Matrix x = new Matrix(k, 1);
		for (int i = k - 1; i > -1; i--)
		{
			x[i, 0] = b[i, 0];
			for (int j = k - 1; j > i; j--)
			{
				x[i, 0] -= A[i, j] * x[j, 0];
			}
			x[i, 0] /= A[i, i];
		}
		return x;
	}

	public static Matrix ZeroMatrix(int iRows, int iCols)
	{
		Matrix matrix = new Matrix(iRows, iCols);
		for (int i = 0; i < iRows; i++)
		{
			for (int j = 0; j < iCols; j++)
			{
				matrix[i, j] = 0.0;
			}
		}
		return matrix;
	}

	public static Matrix IdentityMatrix(int iRows, int iCols)
	{
		Matrix matrix = ZeroMatrix(iRows, iCols);
		for (int i = 0; i < System.Math.Min(iRows, iCols); i++)
		{
			matrix[i, i] = 1.0;
		}
		return matrix;
	}

	public static Matrix RandomMatrix(int iRows, int iCols, int dispersion)
	{
		Random random = new Random();
		Matrix matrix = new Matrix(iRows, iCols);
		for (int i = 0; i < iRows; i++)
		{
			for (int j = 0; j < iCols; j++)
			{
				matrix[i, j] = random.Next(-dispersion, dispersion);
			}
		}
		return matrix;
	}

	public static Matrix Parse(string ps)
	{
		string s = NormalizeMatrixString(ps);
		string[] rows = Regex.Split(s, "\r\n");
		string[] nums = rows[0].Split(' ');
		Matrix matrix = new Matrix(rows.Length, nums.Length);
		try
		{
			for (int i = 0; i < rows.Length; i++)
			{
				nums = rows[i].Split(' ');
				for (int j = 0; j < nums.Length; j++)
				{
					matrix[i, j] = double.Parse(nums[j]);
				}
			}
		}
		catch (FormatException)
		{
			throw new MException("Wrong input format!");
		}
		return matrix;
	}

	public override string ToString()
	{
		string s = "";
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				s = s + string.Format("{0,5:0.00}", mat[i, j]) + " ";
			}
			s += "\r\n";
		}
		return s;
	}

	public static Matrix Transpose(Matrix m)
	{
		Matrix t = new Matrix(m.cols, m.rows);
		for (int i = 0; i < m.rows; i++)
		{
			for (int j = 0; j < m.cols; j++)
			{
				t[j, i] = m[i, j];
			}
		}
		return t;
	}

	public static Matrix Power(Matrix m, int pow)
	{
		if (pow == 0)
		{
			return IdentityMatrix(m.rows, m.cols);
		}
		if (pow == 1)
		{
			return m.Duplicate();
		}
		if (pow == -1)
		{
			return m.Invert();
		}
		Matrix x;
		if (pow < 0)
		{
			x = m.Invert();
			pow *= -1;
		}
		else
		{
			x = m.Duplicate();
		}
		Matrix ret = IdentityMatrix(m.rows, m.cols);
		while (pow != 0)
		{
			if ((pow & 1) == 1)
			{
				ret *= x;
			}
			x *= x;
			pow >>= 1;
		}
		return ret;
	}

	private static void SafeAplusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
	{
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				C[i, j] = 0.0;
				if (xa + j < A.cols && ya + i < A.rows)
				{
					C[i, j] += A[ya + i, xa + j];
				}
				if (xb + j < B.cols && yb + i < B.rows)
				{
					C[i, j] += B[yb + i, xb + j];
				}
			}
		}
	}

	private static void SafeAminusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
	{
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				C[i, j] = 0.0;
				if (xa + j < A.cols && ya + i < A.rows)
				{
					C[i, j] += A[ya + i, xa + j];
				}
				if (xb + j < B.cols && yb + i < B.rows)
				{
					C[i, j] -= B[yb + i, xb + j];
				}
			}
		}
	}

	private static void SafeACopytoC(Matrix A, int xa, int ya, Matrix C, int size)
	{
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				C[i, j] = 0.0;
				if (xa + j < A.cols && ya + i < A.rows)
				{
					C[i, j] += A[ya + i, xa + j];
				}
			}
		}
	}

	private static void AplusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
	{
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				C[i, j] = A[ya + i, xa + j] + B[yb + i, xb + j];
			}
		}
	}

	private static void AminusBintoC(Matrix A, int xa, int ya, Matrix B, int xb, int yb, Matrix C, int size)
	{
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				C[i, j] = A[ya + i, xa + j] - B[yb + i, xb + j];
			}
		}
	}

	private static void ACopytoC(Matrix A, int xa, int ya, Matrix C, int size)
	{
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				C[i, j] = A[ya + i, xa + j];
			}
		}
	}

	private static Matrix StrassenMultiply(Matrix A, Matrix B)
	{
		if (A.cols != B.rows)
		{
			throw new MException("Wrong dimension of matrix!");
		}
		int msize = System.Math.Max(System.Math.Max(A.rows, A.cols), System.Math.Max(B.rows, B.cols));
		Matrix R;
		if (msize < 32)
		{
			R = ZeroMatrix(A.rows, B.cols);
			for (int n = 0; n < R.rows; n++)
			{
				for (int j7 = 0; j7 < R.cols; j7++)
				{
					for (int k2 = 0; k2 < A.cols; k2++)
					{
						R[n, j7] += A[n, k2] * B[k2, j7];
					}
				}
			}
			return R;
		}
		int size = 1;
		int n2 = 0;
		while (msize > size)
		{
			size *= 2;
			n2++;
		}
		int h = size / 2;
		Matrix[,] mField = new Matrix[n2, 9];
		for (int m = 0; m < n2 - 4; m++)
		{
			int z = (int)System.Math.Pow(2.0, n2 - m - 1);
			for (int j2 = 0; j2 < 9; j2++)
			{
				mField[m, j2] = new Matrix(z, z);
			}
		}
		SafeAplusBintoC(A, 0, 0, A, h, h, mField[0, 0], h);
		SafeAplusBintoC(B, 0, 0, B, h, h, mField[0, 1], h);
		StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 2], 1, mField);
		SafeAplusBintoC(A, 0, h, A, h, h, mField[0, 0], h);
		SafeACopytoC(B, 0, 0, mField[0, 1], h);
		StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 3], 1, mField);
		SafeACopytoC(A, 0, 0, mField[0, 0], h);
		SafeAminusBintoC(B, h, 0, B, h, h, mField[0, 1], h);
		StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 4], 1, mField);
		SafeACopytoC(A, h, h, mField[0, 0], h);
		SafeAminusBintoC(B, 0, h, B, 0, 0, mField[0, 1], h);
		StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 5], 1, mField);
		SafeAplusBintoC(A, 0, 0, A, h, 0, mField[0, 0], h);
		SafeACopytoC(B, h, h, mField[0, 1], h);
		StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 6], 1, mField);
		SafeAminusBintoC(A, 0, h, A, 0, 0, mField[0, 0], h);
		SafeAplusBintoC(B, 0, 0, B, h, 0, mField[0, 1], h);
		StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 7], 1, mField);
		SafeAminusBintoC(A, h, 0, A, h, h, mField[0, 0], h);
		SafeAplusBintoC(B, 0, h, B, h, h, mField[0, 1], h);
		StrassenMultiplyRun(mField[0, 0], mField[0, 1], mField[0, 8], 1, mField);
		R = new Matrix(A.rows, B.cols);
		for (int l = 0; l < System.Math.Min(h, R.rows); l++)
		{
			for (int j3 = 0; j3 < System.Math.Min(h, R.cols); j3++)
			{
				R[l, j3] = mField[0, 2][l, j3] + mField[0, 5][l, j3] - mField[0, 6][l, j3] + mField[0, 8][l, j3];
			}
		}
		for (int k = 0; k < System.Math.Min(h, R.rows); k++)
		{
			for (int j4 = h; j4 < System.Math.Min(2 * h, R.cols); j4++)
			{
				R[k, j4] = mField[0, 4][k, j4 - h] + mField[0, 6][k, j4 - h];
			}
		}
		for (int j = h; j < System.Math.Min(2 * h, R.rows); j++)
		{
			for (int j5 = 0; j5 < System.Math.Min(h, R.cols); j5++)
			{
				R[j, j5] = mField[0, 3][j - h, j5] + mField[0, 5][j - h, j5];
			}
		}
		for (int i = h; i < System.Math.Min(2 * h, R.rows); i++)
		{
			for (int j6 = h; j6 < System.Math.Min(2 * h, R.cols); j6++)
			{
				R[i, j6] = mField[0, 2][i - h, j6 - h] - mField[0, 3][i - h, j6 - h] + mField[0, 4][i - h, j6 - h] + mField[0, 7][i - h, j6 - h];
			}
		}
		return R;
	}

	private static void StrassenMultiplyRun(Matrix A, Matrix B, Matrix C, int l, Matrix[,] f)
	{
		int size = A.rows;
		int h = size / 2;
		if (size < 32)
		{
			for (int n = 0; n < C.rows; n++)
			{
				for (int j6 = 0; j6 < C.cols; j6++)
				{
					C[n, j6] = 0.0;
					for (int k2 = 0; k2 < A.cols; k2++)
					{
						C[n, j6] += A[n, k2] * B[k2, j6];
					}
				}
			}
			return;
		}
		AplusBintoC(A, 0, 0, A, h, h, f[l, 0], h);
		AplusBintoC(B, 0, 0, B, h, h, f[l, 1], h);
		StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 2], l + 1, f);
		AplusBintoC(A, 0, h, A, h, h, f[l, 0], h);
		ACopytoC(B, 0, 0, f[l, 1], h);
		StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 3], l + 1, f);
		ACopytoC(A, 0, 0, f[l, 0], h);
		AminusBintoC(B, h, 0, B, h, h, f[l, 1], h);
		StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 4], l + 1, f);
		ACopytoC(A, h, h, f[l, 0], h);
		AminusBintoC(B, 0, h, B, 0, 0, f[l, 1], h);
		StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 5], l + 1, f);
		AplusBintoC(A, 0, 0, A, h, 0, f[l, 0], h);
		ACopytoC(B, h, h, f[l, 1], h);
		StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 6], l + 1, f);
		AminusBintoC(A, 0, h, A, 0, 0, f[l, 0], h);
		AplusBintoC(B, 0, 0, B, h, 0, f[l, 1], h);
		StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 7], l + 1, f);
		AminusBintoC(A, h, 0, A, h, h, f[l, 0], h);
		AplusBintoC(B, 0, h, B, h, h, f[l, 1], h);
		StrassenMultiplyRun(f[l, 0], f[l, 1], f[l, 8], l + 1, f);
		for (int m = 0; m < h; m++)
		{
			for (int j2 = 0; j2 < h; j2++)
			{
				C[m, j2] = f[l, 2][m, j2] + f[l, 5][m, j2] - f[l, 6][m, j2] + f[l, 8][m, j2];
			}
		}
		for (int k = 0; k < h; k++)
		{
			for (int j3 = h; j3 < size; j3++)
			{
				C[k, j3] = f[l, 4][k, j3 - h] + f[l, 6][k, j3 - h];
			}
		}
		for (int j = h; j < size; j++)
		{
			for (int j4 = 0; j4 < h; j4++)
			{
				C[j, j4] = f[l, 3][j - h, j4] + f[l, 5][j - h, j4];
			}
		}
		for (int i = h; i < size; i++)
		{
			for (int j5 = h; j5 < size; j5++)
			{
				C[i, j5] = f[l, 2][i - h, j5 - h] - f[l, 3][i - h, j5 - h] + f[l, 4][i - h, j5 - h] + f[l, 7][i - h, j5 - h];
			}
		}
	}

	public static Matrix StupidMultiply(Matrix m1, Matrix m2)
	{
		if (m1.cols != m2.rows)
		{
			throw new MException("Wrong dimensions of matrix!");
		}
		Matrix result = ZeroMatrix(m1.rows, m2.cols);
		for (int i = 0; i < result.rows; i++)
		{
			for (int j = 0; j < result.cols; j++)
			{
				for (int k = 0; k < m1.cols; k++)
				{
					result[i, j] += m1[i, k] * m2[k, j];
				}
			}
		}
		return result;
	}

	private static Matrix Multiply(double n, Matrix m)
	{
		Matrix r = new Matrix(m.rows, m.cols);
		for (int i = 0; i < m.rows; i++)
		{
			for (int j = 0; j < m.cols; j++)
			{
				r[i, j] = m[i, j] * n;
			}
		}
		return r;
	}

	private static Matrix Add(Matrix m1, Matrix m2)
	{
		if (m1.rows != m2.rows || m1.cols != m2.cols)
		{
			throw new MException("Matrices must have the same dimensions!");
		}
		Matrix r = new Matrix(m1.rows, m1.cols);
		for (int i = 0; i < r.rows; i++)
		{
			for (int j = 0; j < r.cols; j++)
			{
				r[i, j] = m1[i, j] + m2[i, j];
			}
		}
		return r;
	}

	public static string NormalizeMatrixString(string matStr)
	{
		while (matStr.IndexOf("  ") != -1)
		{
			matStr = matStr.Replace("  ", " ");
		}
		matStr = matStr.Replace(" \r\n", "\r\n");
		matStr = matStr.Replace("\r\n ", "\r\n");
		matStr = matStr.Replace("\r\n", "|");
		while (matStr.LastIndexOf("|") == matStr.Length - 1)
		{
			matStr = matStr.Substring(0, matStr.Length - 1);
		}
		matStr = matStr.Replace("|", "\r\n");
		return matStr.Trim();
	}

	public static Matrix operator -(Matrix m)
	{
		return Multiply(-1.0, m);
	}

	public static Matrix operator +(Matrix m1, Matrix m2)
	{
		return Add(m1, m2);
	}

	public static Matrix operator -(Matrix m1, Matrix m2)
	{
		return Add(m1, -m2);
	}

	public static Matrix operator *(Matrix m1, Matrix m2)
	{
		return StrassenMultiply(m1, m2);
	}

	public static Matrix operator *(double n, Matrix m)
	{
		return Multiply(n, m);
	}
}
