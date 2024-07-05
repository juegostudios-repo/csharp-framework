namespace JuegoFramework.Helpers
{
    public abstract class MySqlLib<T> where T : class
    {
        public static async Task<T?> FindOne(Dictionary<string, object?> whereConditions)
        {
            return await SQLManager.FindOne<T>(whereConditions);
        }

        public static async Task<T?> FindOne(object whereConditions)
        {
            return await SQLManager.FindOne<T>(whereConditions);
        }

        public static async Task<List<T>> FindAll(Dictionary<string, object?> whereConditions)
        {
            return await SQLManager.FindAll<T>(whereConditions);
        }

        public static async Task<List<T>> FindAll(object whereConditions)
        {
            return await SQLManager.FindAll<T>(whereConditions);
        }

        public static async Task<long> Insert(Dictionary<string, object?> insertData)
        {
            return await SQLManager.Insert<T>(insertData);
        }

        public static async Task<long> Insert(object insertData)
        {
            return await SQLManager.Insert<T>(insertData);
        }


        public static async Task<long> Insert(T insertData)
        {
            return await SQLManager.Insert<T>(insertData);
        }

        public static async Task<List<long>> Insert(List<T> insertData)
        {
            return await SQLManager.Insert<T>(insertData);
        }

        public static async Task<int> Update(Dictionary<string, object?> whereConditions, Dictionary<string, object?> updateData)
        {
            return await SQLManager.Update<T>(whereConditions, updateData);
        }

        public static async Task<int> Update(object whereConditions, object updateData)
        {
            return await SQLManager.Update<T>(whereConditions, updateData);
        }
    }
}
