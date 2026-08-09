using System.Collections.Generic;
using UnityEngine;

namespace Artemis.Inventory
{
    /// <summary>Algebraic (Kåsa) least-squares circle fit with one robust refit pass.</summary>
    public static class CircleFit
    {
        public struct Result
        {
            public bool    Ok;
            public Vector2 Center;
            public float   Radius;
            public float   RmsResidual;
            public int     InlierCount;
        }

        public static Result Fit(IReadOnlyList<Vector2> pts)
        {
            var r = FitOnce(pts);
            if (!r.Ok) return r;

            float tol = Mathf.Max(r.RmsResidual * 2.5f, 0.003f);
            var inliers = new List<Vector2>(pts.Count);
            foreach (var p in pts)
                if (Mathf.Abs((p - r.Center).magnitude - r.Radius) <= tol) inliers.Add(p);

            if (inliers.Count >= 3 && inliers.Count < pts.Count)
            {
                var r2 = FitOnce(inliers);
                if (r2.Ok) { r2.InlierCount = inliers.Count; return r2; }
            }
            r.InlierCount = pts.Count;
            return r;
        }

        private static Result FitOnce(IReadOnlyList<Vector2> pts)
        {
            var res = new Result { Ok = false };
            int n = pts.Count;
            if (n < 3) return res;

            double Sx=0,Sy=0,Sxx=0,Syy=0,Sxy=0,Sxz=0,Syz=0,Sz=0;
            foreach (var p in pts)
            {
                double x=p.x, y=p.y, z=x*x+y*y;
                Sx+=x; Sy+=y; Sxx+=x*x; Syy+=y*y; Sxy+=x*y; Sxz+=x*z; Syz+=y*z; Sz+=z;
            }

            double[,] M = { { Sxx, Sxy, Sx }, { Sxy, Syy, Sy }, { Sx, Sy, n } };
            double[] rhs = { -Sxz, -Syz, -Sz };
            if (!Solve3(M, rhs, out double A, out double B, out double C)) return res;

            double cx=-A/2.0, cy=-B/2.0, r2=cx*cx+cy*cy-C;
            if (r2 <= 0) return res;
            double radius = System.Math.Sqrt(r2);

            double acc=0;
            foreach (var p in pts)
            {
                double dx=p.x-cx, dy=p.y-cy, d=System.Math.Sqrt(dx*dx+dy*dy)-radius;
                acc+=d*d;
            }
            res.Ok=true; res.Center=new Vector2((float)cx,(float)cy);
            res.Radius=(float)radius; res.RmsResidual=(float)System.Math.Sqrt(acc/n);
            return res;
        }

        private static bool Solve3(double[,] m, double[] b, out double x0, out double x1, out double x2)
        {
            x0=x1=x2=0;
            double[,] a = { {m[0,0],m[0,1],m[0,2],b[0]}, {m[1,0],m[1,1],m[1,2],b[1]}, {m[2,0],m[2,1],m[2,2],b[2]} };
            for (int col=0; col<3; col++)
            {
                int piv=col; double best=System.Math.Abs(a[col,col]);
                for (int r=col+1;r<3;r++){ double v=System.Math.Abs(a[r,col]); if(v>best){best=v;piv=r;} }
                if (best<1e-12) return false;
                if (piv!=col) for(int k=0;k<4;k++){ var t=a[col,k];a[col,k]=a[piv,k];a[piv,k]=t; }
                for (int r=0;r<3;r++){ if(r==col) continue; double f=a[r,col]/a[col,col]; for(int k=col;k<4;k++) a[r,k]-=f*a[col,k]; }
            }
            x0=a[0,3]/a[0,0]; x1=a[1,3]/a[1,1]; x2=a[2,3]/a[2,2];
            return true;
        }
    }
}
