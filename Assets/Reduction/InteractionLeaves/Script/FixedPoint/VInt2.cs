namespace UnityEngine.PBD
{
    public struct VInt2
    {
        public int x;
        public int y;

        public VInt2(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public VInt2(float x, float y)
        {
            this.x = FixedPointUtils.Float2Fixed(x);
            this.y = FixedPointUtils.Float2Fixed(y);
        }
        
        public VInt2(VInt x, VInt y)
        {
            this.x = x.i;
            this.y = y.i;
        }
    }
}