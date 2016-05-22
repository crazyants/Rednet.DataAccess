using System;
using System.Collections;
using System.Collections.Generic;
#if !PCL
using System.Data;
using System.Data.Common;
using System.Security.Cryptography.X509Certificates;
using Rednet.DataAccess.Dapper;
#endif
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
//using PCLStorage;
using Rednet.DataAccess.FastMember;

namespace Rednet.DataAccess
{

    public enum DatabaseType
    {
        Nenhum = 0,
        MySQL = 1,
        Oracle = 2,
        PostgreSQL = 3,
        SQLite = 4,
        SQLServer = 5
    }

    public interface IDataFunctions
    {
        DatabaseType DatabaseType { get; }
        string Name { get; set; }
#if !PCL
        IDbConnection Connection { get; }
#endif
        string GetConnectionString();
        string GetSqlLastIdentity();
        string GetDateTimeFormat();
        string ToJson();
        List<DbColumnDef> GetColumnsDef(string tableName);
        void SaveDdlScript<TDatabaseObject>(string path) where TDatabaseObject : IDatabaseObject;
        bool CheckDdlScript<TDatabaseObject>(string path) where TDatabaseObject : IDatabaseObject;
        bool AlterTable<TDatabaseObject>() where TDatabaseObject : IDatabaseObject;
        bool TableExists<TDatabaseObject>() where TDatabaseObject : IDatabaseObject;
        bool TableExists(string tableName);
        void RenameTable(string fromName, string toName);
        string PrefixParameter { get; }
#if !PCL
        TDatabaseObject Load<TDatabaseObject>(DboCommand command);
        TDatabaseObject Load<TDatabaseObject>(string sqlStatement, object dynamicParameters = null);
        List<TDatabaseObject> Query<TDatabaseObject>(DboCommand command, bool useFieldNames = true);
        List<TDatabaseObject> Query<TDatabaseObject>(string sqlStatement, object dynamicParameters = null);
        TDatabaseObject ReloadMe<TDatabaseObject>(DboCommand command);
        CrudReturn SaveChanges<TDatabaseObject>(TDatabaseObject objectToSave, bool ignoreAutoIncrementField = true, bool doNotUpdateWhenExists = false) where TDatabaseObject : IDatabaseObject;
        CrudReturn Insert<TDatabaseObject>(TDatabaseObject objectToInsert, bool ignoreAutoIncrementField = true) where TDatabaseObject : IDatabaseObject;
        CrudReturn Update<TDatabaseObject>(TDatabaseObject objectToUpdate) where TDatabaseObject : IDatabaseObject;
        CrudReturn Delete<TDatabaseObject>(TDatabaseObject objectToDelete) where TDatabaseObject : IDatabaseObject;
        CrudReturn DeleteAll<TDatabaseObject>(DboCommand command);
        bool Exists(string sql, object obj);
        bool Exists(DboCommand command);
#endif
        CrudReturn ExecuteStatement(string statement);
    }

    public enum CrudStatus
    {
        None,
        Ok,
        Fail
    }

#if!PCL
    [Serializable]
#endif
    public class CrudReturn
    {
        public object ReturnData { get; set; }
        public CrudStatus ReturnStatus { get; set; }
        public string ReturnMessage { get; set; }
        public int RecordsAffected { get; set; }
        public SqlStatementsTypes ChangeType { get; set; }
    }

    public abstract class DataFunctions<T> : IDataFunctions
    {

        private readonly DatabaseType m_DatabaseType;
        private static Dictionary<string, Action> m_PopulateActions = new Dictionary<string, Action>(); 

        protected DataFunctions(DatabaseType databaseType)
        {
            m_DatabaseType = databaseType;
        }

        public Dictionary<string, object> ToDictionary(object value)
        {
            var _data = DatabaseObject<object>.ToJson(value);
            var _ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(_data, new JsonSerializerSettings() { ContractResolver = new SerializableContractResolver(), Converters = { new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" } } });
            return _ret;
        }

#if !PCL
        public TDatabaseObject Load<TDatabaseObject>(DboCommand command)
        {
            return this.Query<TDatabaseObject>(command).FirstOrDefault();
        }

        public TDatabaseObject Load<TDatabaseObject>(string sqlStatement, object dynamicParameters = null)
        {
            return this.Query<TDatabaseObject>(sqlStatement, dynamicParameters).FirstOrDefault();
        }

        public List<TDatabaseObject> Query<TDatabaseObject>(string sqlStatement, object dynamicParameters = null)
        {
            try
            {
                IDataReader _rows = null;
                if (dynamicParameters != null)
                {
                    var _parameters = ToDictionary(dynamicParameters);
                    _rows = GetDataReader(sqlStatement, _parameters);
                }
                else
                {
                    _rows = GetDataReader(sqlStatement);
                }
                var _ret = ReadRowsFromReader<TDatabaseObject>(_rows);
                return _ret;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
        }

        public List<TDatabaseObject> Query<TDatabaseObject>(DboCommand command, bool useFieldNames = true)
        {

            try
            {

                var _ret = new List<TDatabaseObject>();

                if (useFieldNames)
                {

                    var _rows = GetDataReader(command.GetCommandDefinition());

                    _ret = ReadRowsFromReader<TDatabaseObject>(_rows);

                }
                else
                {
                    using (var _conn = this.Connection)
                    {
                        _ret = _conn.Query<TDatabaseObject>(command.GetCommandDefinition()).ToList();
                        _conn.Close();
                    }
                }

                return _ret;

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
        }

        public TDatabaseObject ReloadMe<TDatabaseObject>(DboCommand command)
        {
            var _rows = GetDataReader(command.GetCommandDefinition());

            var _ret = ReadRowsFromReader<TDatabaseObject>(_rows);

            return _ret.FirstOrDefault();
        }

        public CrudReturn SaveChanges<TDatabaseObject>(TDatabaseObject objectToSave, bool ignoreAutoIncrementField = true, bool doNotUpdateWhenExists = false) where TDatabaseObject : IDatabaseObject
        {
            var _table = TableDefinition.GetTableDefinition(typeof(TDatabaseObject));

            var _ret = new CrudReturn 
            {
                ReturnStatus = CrudStatus.Ok, 
                RecordsAffected = -1, 
                ChangeType = SqlStatementsTypes.None, 
                ReturnMessage = "Dados atualizados com sucesso!"
            };

            try
            {

                using (var _connection = _table.DefaultDataFunction.Connection)
                {

                    var _sql = "";
                    var _backEndField = _table.Fields.Select(f => f.Value).FirstOrDefault(f => f.AutomaticValue != AutomaticValue.None);
                    var _data = objectToSave.ToDictionary();

                    var _exists = _connection.ExecuteScalar<long>(_table.GetSqlSelectForCheck(), _data);

                    if (_exists == 0)
                    {
                        _ret.ChangeType = SqlStatementsTypes.Insert;
                        if (_backEndField != null)
                        {
                            _sql = _table.GetSqlInsert(ignoreAutoIncrementField);
                            int _lastId = this.DoInsert(_table, _connection, _sql, _data);
                            objectToSave.SetObjectFieldValue(_backEndField.Name, _lastId);
                            _ret.RecordsAffected = 1;
                        }
                        else
                        {
                            objectToSave.SetIdFields();
                            _sql = _table.GetSqlInsert(ignoreAutoIncrementField);
                            _ret.RecordsAffected = _connection.Execute(_sql, _data);
                        }
                    }
                    else
                    {
                        if (!doNotUpdateWhenExists)
                        {
                            _ret.ChangeType = SqlStatementsTypes.Update;
                            _sql = _table.GetSqlUpdate();
                            _ret.RecordsAffected = _connection.Execute(_sql, _data);
                        }
                    }

                    _ret.ReturnData = objectToSave;

                    _connection.Close();

                }
            }
            catch (Exception ex)
            {
                _ret.ReturnMessage = string.Format("{0}\r\n{1}", ex.Message, ex.StackTrace);
                _ret.ReturnStatus = CrudStatus.Fail;
            }

            return _ret;
        }

        public CrudReturn Insert<TDatabaseObject>(TDatabaseObject objectToInsert, bool ignoreAutoIncrementField = true) where TDatabaseObject : IDatabaseObject
        {
            var _table = TableDefinition.GetTableDefinition(typeof(TDatabaseObject));

            var _ret = new CrudReturn
            {
                ReturnStatus = CrudStatus.Ok,
                RecordsAffected = -1,
                ChangeType = SqlStatementsTypes.Insert,
                ReturnMessage = "Dados atualizados com sucesso!"
            };

            try
            {
                using (var _connection = _table.DefaultDataFunction.Connection)
                {

                    var _sql = "";
                    var _data = objectToInsert.ToDictionary();
                    var _backEndField = _table.Fields.Select(f => f.Value).FirstOrDefault(f => f.AutomaticValue != AutomaticValue.None);

                    if (_backEndField != null)
                    {
                        _sql = _table.GetSqlInsert(ignoreAutoIncrementField);
                        int _lastId = this.DoInsert(_table, _connection, _sql, _data);
                        objectToInsert.SetObjectFieldValue(_backEndField.Name, _lastId);
                        _ret.RecordsAffected = 1;
                    }
                    else
                    {
                        objectToInsert.SetIdFields();
                        _sql = _table.GetSqlInsert(ignoreAutoIncrementField);
                        _ret.RecordsAffected = _connection.Execute(_sql, _data);
                    }

                    _ret.ReturnData = objectToInsert;

                    _connection.Close();

                }
            }
            catch (Exception ex)
            {
                _ret.ReturnMessage = string.Format("{0}\r\n{1}", ex.Message, ex.StackTrace);
                _ret.ReturnStatus = CrudStatus.Fail;
            }

            return _ret;

        }

        public CrudReturn Update<TDatabaseObject>(TDatabaseObject objectToUpdate) where TDatabaseObject : IDatabaseObject
        {
            
            var _table = TableDefinition.GetTableDefinition(typeof(TDatabaseObject));
            var _ret = new CrudReturn
            {
                ReturnStatus = CrudStatus.Ok,
                RecordsAffected = -1,
                ChangeType = SqlStatementsTypes.Update,
                ReturnMessage = "Dados atualizados com sucesso!"
            };

            try
            {

                using (var _connection = _table.DefaultDataFunction.Connection)
                {
                    var _sql = "";
                    var _data = objectToUpdate.ToDictionary();

                    _sql = _table.GetSqlUpdate();
                    _ret.RecordsAffected = _connection.Execute(_sql, _data);
                    _ret.ReturnData = objectToUpdate;

                    _connection.Close();

                }
            }
            catch (Exception ex)
            {
                _ret.ReturnMessage = string.Format("{0}\r\n{1}", ex.Message, ex.StackTrace);
                _ret.ReturnStatus = CrudStatus.Fail;
            }

            return _ret;

        }

        public CrudReturn Delete<TDatabaseObject>(TDatabaseObject objectToDelete) where TDatabaseObject : IDatabaseObject
        {
            var _table = TableDefinition.GetTableDefinition(typeof(TDatabaseObject));
            var _ret = new CrudReturn
            {
                ReturnStatus = CrudStatus.Ok,
                RecordsAffected = -1,
                ChangeType = SqlStatementsTypes.Delete,
                ReturnMessage = "Dados exclu�dos com sucesso!"
            };

            try
            {

                using (var _connection = _table.DefaultDataFunction.Connection)
                {

                    var _sql = _table.GetSqlDelete();

                    _ret.RecordsAffected = _connection.Execute(_sql, objectToDelete);

                    _connection.Close();

                }
            }
            catch (Exception ex)
            {
                _ret.ReturnMessage = string.Format("{0}\r\n{1}", ex.Message, ex.StackTrace);
                _ret.ReturnStatus = CrudStatus.Fail;
            }

            return _ret;

        }

        public CrudReturn DeleteAll<TDatabaseObject>(DboCommand command)
        {
            var _table = TableDefinition.GetTableDefinition(typeof(TDatabaseObject));
            var _ret = new CrudReturn
            {
                ReturnStatus = CrudStatus.Ok,
                RecordsAffected = -1,
                ChangeType = SqlStatementsTypes.DeleteAll,
                ReturnMessage = "Dados exclu�dos com sucesso!"
            };

            try
            {
                using (var _connection = _table.DefaultDataFunction.Connection)
                {
                    _ret.RecordsAffected = _connection.Execute(command.GetCommandDefinition());
                    _connection.Close();
                }
            }
            catch (Exception ex)
            {
                _ret.ReturnMessage = string.Format("{0}\r\n{1}", ex.Message, ex.StackTrace);
                _ret.ReturnStatus = CrudStatus.Fail;
            }

            return _ret;

        }

        public bool Exists(string sql, object obj)
        {
            using (var _db = this.Connection)
            {
                var _exists = _db.ExecuteScalar<long>(sql, obj);
                _db.Close();
                return _exists != 0;
            }
        }

        public bool Exists(DboCommand command)
        {
            using (var _db = this.Connection)
            {
                var _exists = _db.ExecuteScalar<long>(command.GetCommandDefinition());
                _db.Close();
                return _exists != 0;
            }
        }

        private int DoInsert(TableDefinition table, IDbConnection connection, string sql, object objectParameters)
        {
            var _ret = connection.Query<int>(sql + "\n" + table.DefaultDataFunction.GetSqlLastIdentity(), objectParameters).First();
            return _ret;
        }

#endif

#if !PCL
        internal IDataReader GetDataReader(string sql, object parameters = null)
        {
            return this.Connection.ExecuteReader(new CommandDefinition(sql, parameters), CommandBehavior.CloseConnection);
        }

        internal IDataReader GetDataReader(CommandDefinition command)
        {
            return this.Connection.ExecuteReader(command, CommandBehavior.CloseConnection);
        }

        private static List<TDatabaseObject> ReadRowsFromReader<TDatabaseObject>(IDataReader reader)// where TDatabaseObject : IDatabaseObject
        {
            var _ret = new List<TDatabaseObject>();
            var _checkList = new Dictionary<string, object>();

            using (reader)
            {
                while (reader.Read())
                {
                    var _row = new Dictionary<string, object>();
                    for (var _i = 0; _i < reader.FieldCount; _i++)
                    {
                        var _name = reader.GetName(_i);
                        if (_row.ContainsKey(_name)) continue;
                        _row.Add(_name, reader.IsDBNull(_i) ? null : reader.GetValue(_i));
                    }
                    var _data = PopulateData<TDatabaseObject>(_row, _checkList);
                    if (_data != null)
                        _ret.Add(_data);
                }

                reader.Close();
            }
            return _ret;
        }
#endif

        public static TDatabaseObject PopulateData<TDatabaseObject>(IDictionary<string, object> row, IDictionary<string, object> checkList = null, string baseKey = null, string baseName = null)// where TDatabaseObject : IDatabaseObject
        {
#if !PCL
            object _ret = null;
            var _type = typeof (TDatabaseObject);

            if (_type.IsPrimitive || _type.GetInterface("IDatabaseObject") == null)
                return default(TDatabaseObject);

            var _name = _type.Name;

            if (!DatabaseObjectShared.PrimaryKeys.ContainsKey(_name))
                DatabaseObjectShared.PrimaryKeys.Add(_name, GetPrimaryKeyFields<TDatabaseObject>().Select(pk => pk.Name).ToArray());

            var _list = checkList ?? new Dictionary<string, object>();

            var _acessor = TypeAccessor.Create(_type);
            var _members = _acessor.GetMembers().Where(a => a.CanWrite);
            var _fields = new List<string>();
            var _baseName = (baseName == null ? "" : baseName + "_");

            _fields.AddRange(from _member in _members where !row.ContainsKey(_baseName + _member.Name) select _member.Name);

            //var _kchk = (baseKey == null ? "" : baseKey + "_") + _name + "_" + row.Where(_k => CachedObjects.PrimaryKeys[_name].Contains(_k.Key)).Aggregate("", (_current, _k) => _current + (_k.Value == null ? "null" : _k.Value.ToString()));
            var _kchk = (baseKey == null ? "" : baseKey + "_") + _name;

            var _keys = DatabaseObjectShared.PrimaryKeys[_name];
            foreach (var _key in _keys)
            {
                var _k = (_baseName + _key);
                var _value = row[_k];
                _kchk += "_" + (_value == null ? "null" : _value.ToString());
            }

            if (_list.ContainsKey(_kchk))
            {
                _ret = _list[_kchk];
            }

            var _object = _ret ?? _acessor.CreateNew();
            foreach (var _member in _members)
            {
                var _sqlName = _baseName + _member.Name;

                if (_fields.Contains(_member.Name))
                {
                    var _t = _member.Type.IsGenericType ? _member.Type.GenericTypeArguments[0] : _member.Type;
                    try
                    {
                        // este metodo era utilizado quando esta function estava em DatabaseObject
                        //var _v = typeof(DataFunctions<T>).MakeGenericType(_t).GetMethod("PopulateData").Invoke(null, new object[] { row, _list, _kchk, _sqlName });
                        var _v = typeof(DataFunctions<T>).GetMethod("PopulateData").MakeGenericMethod(_t).Invoke(null, new object[] { row, _list, _kchk, _sqlName });
                        if (_v == null) continue;
                        if (_member.Type.IsGenericType)
                        {
                            _acessor[_object, _member.Name] = _acessor[_object, _member.Name] ?? TypeAccessor.Create(_member.Type).CreateNew();

                            (_acessor[_object, _member.Name] as IList).Add(_v);

                        }
                        else
                        {
                            _acessor[_object, _member.Name] = _v;
                        }
                    }
                    catch (Exception ex)
                    {
                        var _ex = 10;
                    }
                }
                else
                {
                    var _value = row[_sqlName];
                    // tenta jogar o valor nulo, se recusar significa que � um campo requerido ent�o o registro est� nulo
                    try
                    {
                        _acessor[_object, _member.Name] = _value;
                    }
                    catch (NullReferenceException nrex)
                    {
                        // se o campo a ser definido pertencer aos campos da chave primaria, ent�o definimos como nulo
                        if (DatabaseObjectShared.PrimaryKeys[_name].Contains(_member.Name))
                        {
                            _object = null;
                            break;
                        }
                    }
                    catch (InvalidCastException icex)
                    {
                        if (Nullable.GetUnderlyingType(_member.Type) != null)
                            _acessor[_object, _member.Name] = Convert.ChangeType(_value, Nullable.GetUnderlyingType(_member.Type));
                        else
                        {
                            if (_member.Type.IsEnum)
                                _acessor[_object, _member.Name] = Convert.ChangeType(_value, typeof(int));
                            else
                                _acessor[_object, _member.Name] = Convert.ChangeType(_value, _member.Type);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message, ex);
                    }
                }
            }


            if (_list.ContainsKey(_kchk)) return default(TDatabaseObject);

            _list.Add(_kchk, _object);

            return (TDatabaseObject)_object;
#else
            return default(TDatabaseObject);
#endif
        }

        private static IEnumerable<FieldDefAttribute> GetPrimaryKeyFields<TDatabaseObject>()// where TDatabaseObject : IDatabaseObject
        {
#if !PCL
            return TableDefinition.GetTableDefinition(typeof(TDatabaseObject)).Fields.Where(f => f.Value.IsPrimaryKey).Select(f => f.Value);
#else
            return new List<FieldDefAttribute>();
#endif
        }

        public DatabaseType DatabaseType
        {
            get { return m_DatabaseType; }
        }

#if !PCL
        public abstract IDbConnection Connection { get; }
#endif

        public CrudReturn ExecuteStatement(string sqlStatement)
        {
            var _ret = new CrudReturn
            {
                RecordsAffected = -1,
                ChangeType = SqlStatementsTypes.UnknownStatement,
                ReturnStatus = CrudStatus.None
            };
#if !PCL
            try
            {
                using (var _conn = this.Connection)
                {
                    _ret.RecordsAffected = _conn.Execute(sqlStatement);
                }
            }
            catch (Exception ex)
            {
                _ret.ReturnMessage = string.Format("{0}\r\n{1}", ex.Message, ex.StackTrace);
                _ret.ReturnStatus = CrudStatus.Fail;
            }
#endif
            return _ret;
        }

        public abstract string GetConnectionString();
        public abstract string GetSqlLastIdentity();
        public abstract string GetDateTimeFormat();
        public abstract List<DbColumnDef> GetColumnsDef(string tableName);

        protected virtual bool CheckTableExists(string tableName)
        {
            return false;
        }

        public string Name { get; set; }
        private bool m_Pooling = true;
        public bool Pooling
        {
            get { return m_Pooling; }
            set { m_Pooling = value; }
        }
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" });
        }

        public static T FromJson(string jsonData)
        {
            return JsonConvert.DeserializeObject<T>(jsonData, new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" });
        }

        void IDataFunctions.SaveDdlScript<TDatabaseObject>(string path)
        {
            SaveDdlScript<TDatabaseObject>(path);
        }

        bool IDataFunctions.CheckDdlScript<TDatabaseObject>(string path)
        {
            return CheckDdlScript<TDatabaseObject>(path);
        }

        bool IDataFunctions.AlterTable<TDatabaseObject>()
        {
            return AlterTable<TDatabaseObject>();
        }

        public bool TableExists<TDatabaseObject>() where TDatabaseObject : IDatabaseObject
        {
            var _name = typeof (TDatabaseObject).Name;
            return this.TableExists(_name);
        }

        public bool TableExists(string tableName)
        {
            return this.CheckTableExists(tableName);
        }

        public virtual void RenameTable(string fromName, string toName)
        {
            throw new NotImplementedException();
        }

        public virtual string PrefixParameter
        {
            get { return "@"; }
        }

        public static void SaveDdlScript<TDatabaseObject>(string path) where TDatabaseObject : IDatabaseObject
        {
            var _ddl = TableDefinition.GetTableDefinition(typeof(TDatabaseObject)).GetScriptCreateTable();
            var _file = Path.Combine(path, DatabaseObject<TDatabaseObject>.Name + ".sql");
#if !PCL
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            if (File.Exists(_file))
                File.Delete(_file);

            File.WriteAllText(_file, _ddl);
#endif
        }

        public static bool CheckDdlScript<TDatabaseObject>(string path) where TDatabaseObject : IDatabaseObject
        {
#if !PCL

            var _file = Path.Combine(path, DatabaseObject<TDatabaseObject>.Name + ".sql");

            if (!File.Exists(_file))
            {
                return false;
            }
            else
            {
                var _oldDdl = File.ReadAllText(_file);
                var _curDdl = TableDefinition.GetTableDefinition(typeof(TDatabaseObject)).GetScriptCreateTable();
                var _ret = _oldDdl.Equals(_curDdl);
                return _ret;
            }
#else
            return false;
#endif
        }

        public bool AlterTable<TDatabaseObject>() where TDatabaseObject : IDatabaseObject
        {
#if !PCL

            try
            {
                var _table = TableDefinition.GetTableDefinition(typeof (TDatabaseObject));
                var _backup = BackupData<TDatabaseObject>();

                DropTable<TDatabaseObject>();

                using (var _conn = _table.DefaultDataFunction.Connection)
                {
                    _conn.Execute(_table.GetScriptCreateTable());
                }

                foreach (var _bkp in _backup)
                {
                    (_bkp as IDatabaseObject).SaveChanges();
                }

                return true;

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }

#else
            return false;
#endif
        }

        public static List<TDatabaseObject> BackupData<TDatabaseObject>()
        {
#if !PCL
            var _name = DatabaseObject<TDatabaseObject>.Name;
            try
            {
                var _sql = string.Format("select * from {0}", _name);
                return DatabaseObject<TDatabaseObject>.Query(_sql);
            }
            catch (Exception ex)
            {
                if (ex.Message.ToLower().Contains("no such table"))
                    return new List<TDatabaseObject>();

                throw new Exception(string.Format("Ocorreu um erro ao fazer o backup da tabela [{0}].\r\n\r\nErro: {1}\r\n\r\nEm: {2}", DatabaseObject<TDatabaseObject>.Name, ex.Message, ex.StackTrace), ex);
            }
#else
            return new List<TDatabaseObject>();
#endif
        }

        private void DropTable<TDatabaseObject>()
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof (TDatabaseObject));

            try
            {
                using (var _conn = _table.DefaultDataFunction.Connection)
                {
                    _conn.Execute(_table.GetScriptDropTable());
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
#endif
        }
    }

    public class DbColumnDef
    {
        public int cid { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public int notnull { get; set; }
        public string dflt_value { get; set; }
        public int pk { get; set; }
    }

    public class TableInfo
    {
        public string type { get; set; }
        public string name { get; set; }
        public string tbl_name { get; set; }
        public string rootpage { get; set; }
        public string sql { get; set; }
    }
}