﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
//using System.Linq.Dynamic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
#if !PCL
using Rednet.DataAccess.FastMember;
using Rednet.DataAccess.Dapper;
using System.Data;
using System.Data.Common;
#endif

namespace Rednet.DataAccess
{

#if !PCL
    [Serializable]
#endif
    public abstract class DatabaseObject<T> : IDatabaseObject, INotifyPropertyChanged
    {

#if !PCL
        [field: NonSerialized]
#endif
        public event EventHandler<NotifyRecordChangesEventArgs> NotifyRecordChangesAfter;
#if !PCL
        [field: NonSerialized]
#endif
        public event EventHandler<NotifyRecordChangesEventArgs> NotifyRecordChangesBefore;

        private static List<SqlStatements> m_SqlList = new List<SqlStatements>();
        private static object m_LockObject = new object();

#if !PCL
        [field: NonSerialized]
#endif
        private Func<bool> m_ValidateDataFunction = null;

        protected virtual void OnBeforeSaveData(NotifyRecordChangesEventArgs e)
        {
            this.NotifyRecordChangesBefore?.Invoke(this, e);
        }

        protected virtual void OnAfterSaveData(NotifyRecordChangesEventArgs e)
        {
            this.NotifyRecordChangesAfter?.Invoke(this, e);
        }

        protected virtual void OnBeforeDeleteData(NotifyRecordChangesEventArgs e)
        {
            this.NotifyRecordChangesBefore?.Invoke(this, e);
        }

        protected virtual void OnAfterDeleteData(NotifyRecordChangesEventArgs e)
        {
            this.NotifyRecordChangesAfter?.Invoke(this, e);
        }

        protected virtual bool OnValidateData()
        {
            var _ret = this.GetValidateDataFunction().Invoke();

            return _ret;
        }

        private Func<bool> GetValidateDataFunction()
        {
#if !PCL
            return m_ValidateDataFunction ?? (m_ValidateDataFunction = (() =>
            {
                var _table = TableDefinition.GetTableDefinition(typeof(T));
                var _rules = _table.Rules.Select(r => r.Value);
                var _validatedFields = new List<ValidatedField>();

                foreach (var _rule in _rules)
                {
                    if (!_rule.IsForValidate) continue;
                    var _prop = _table.BaseType.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(f => f.Name == _rule.Name);
                    if (!_rule.Validate(_prop.GetValue(this)))
                        _validatedFields.Add(new ValidatedField() { FieldMessage = _rule.ValidationText, FieldName = _rule.Name });
                }

                if (_validatedFields.Count > 0)
                {
                    if (this.ErrorOnValidateData != null)
                        this.ErrorOnValidateData(this, new ErrorOnValidateDataEventArgs(_validatedFields.ToArray()));

                    return false;
                }

                return true;
            }));
#else
            return new Func<bool>(() => false);
#endif
        }

        private void SetValidateDataFunction(Func<bool> value)
        {
            m_ValidateDataFunction = value;
        }

#if !PCL
        [field: NonSerialized]
#endif
        public event ErrorOnSaveOrDeleteEventHandler ErrorOnSaveOrDelete;

#if !PCL
        [field: NonSerialized]
#endif
        public event ErrorOnValidateDataEventHandler ErrorOnValidateData;

#if !PCL
        [field: NonSerialized]
#endif
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var _handler = PropertyChanged;
            if (_handler != null) _handler(this, new PropertyChangedEventArgs(propertyName));
        }

        public DatabaseObject()
        {
        }

        public static T CreateInstance()
        {
            return Activator.CreateInstance<T>();
        }

        string IDatabaseObject.Name
        {
            get { return DatabaseObject<T>.Name; }
        }

        public static IEnumerable<FieldDefAttribute> GetFields()
        {
            var _table = TableDefinition.GetTableDefinition(typeof (T));
            return _table.Fields.Select(f => f.Value);
        }

#if !PCL
        [System.ComponentModel.Browsable(false)]
#endif
        [JsonIgnore]
        [FieldDef(DisplayOnForm = false, DisplayOnGrid = false, IgnoreForSave = true, IsInternal = true)]
        public IEnumerable<FieldDefAttribute> Fields
        {
            get
            {
                return GetFields();
            }
        }

        [FieldDef(DisplayOnForm = false, DisplayOnGrid = false, IgnoreForSave = true)]
        public static string Name
        {
            get
            {
#if !PCL
                var _table = TableDefinition.GetTableDefinition(typeof(T));
                var _def = _table.ObjectDefAttribute;
                var _type = typeof(T);
                return _def.PrefixTableNameWithDatabaseName ? string.Format("{0}.{1}", _def.DatabaseName, _type.Name) : _type.Name;
#else
                return "";
#endif
            }
        }

        public string ToJson(bool compressString = false)
        {
            var _ret = JsonConvert.SerializeObject(this, new JsonSerializerSettings() {ContractResolver = new SerializableContractResolver(), Converters = {new NumberConverter(), new IsoDateTimeConverter() {DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff"}}});
            
            if (compressString)
                _ret = _ret.CompressString();

            return _ret;

        }

        public static Dictionary<string, object> ToDictionary(object value)
        {
            var _data = DatabaseObject<object>.ToJson(value);
            var _ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(_data, new JsonSerializerSettings() { ContractResolver = new SerializableContractResolver(), Converters = { new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" } } });
            return _ret;
        }

        public Dictionary<string, object> ToDictionary()
        {
            return ToDictionary(this);
        }

        public static string ToJson(T data, bool compressString = false)
        {
            var _ret = JsonConvert.SerializeObject(data, new JsonSerializerSettings() {ContractResolver = new SerializableContractResolver(), Converters = {new NumberConverter(), new IsoDateTimeConverter() {DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff"}}});

            if (compressString)
                _ret = _ret.CompressString();
            
            return _ret;
        }

        public static string ToJson(List<T> data, bool compressString = false)
        {
            var _ret = JsonConvert.SerializeObject(data, new JsonSerializerSettings() { ContractResolver = new SerializableContractResolver(), Converters = { new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" } } });

            if (compressString)
                _ret = _ret.CompressString();

            return _ret;
        }

#if !PCL
        public static object FromJsonType(Type type, string jsonData, bool decompressString = false)
        {
            var _object = typeof (DatabaseObject<>).MakeGenericType(type);
            var _method = _object.GetMethod("FromJson");
            return _method.Invoke(null, new object[] { jsonData, decompressString });
        }
#endif

        public static T FromJson(string jsonData, bool decompressString = false)
        {
            var _data = jsonData;

            if (decompressString)
                _data = _data.DecompressString();

            return JsonConvert.DeserializeObject<T>(_data, new JsonSerializerSettings() { ContractResolver = new SerializableContractResolver(), Converters = { new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" } } });
        }

        public static List<T> FromJsonList(string jsonData, bool decompressString = false)
        {
            var _data = jsonData;

            if (decompressString)
                _data = _data.DecompressString();

            return JsonConvert.DeserializeObject<List<T>>(_data, new JsonSerializerSettings() { ContractResolver = new SerializableContractResolver(), Converters = { new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" } } });
        }

        public T Clone()
        {
            return JsonConvert.DeserializeObject<T>(this.ToJson(), new JsonSerializerSettings() { ContractResolver = new SerializableContractResolver(), Converters = { new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" } } });
        }

        public TTarget CloneTo<TTarget>()
        {
            var _json = this.ToJson();
            var _ret = JsonConvert.DeserializeObject<TTarget>(_json, new JsonSerializerSettings() { Converters = { new NumberConverter(), new IsoDateTimeConverter() { DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff" } } });
            return _ret;
        }

        public string GetScriptInsert()
        {
            var _data = this.ToDictionary();
            return TableDefinition.GetTableDefinition(typeof(T)).GetScriptInsert(_data);
        }

        public string GetScriptUpdate()
        {
            var _data = this.ToDictionary();
            return TableDefinition.GetTableDefinition(typeof(T)).GetScriptUpdate(_data);
        }

        public string GetScriptDelete()
        {
            var _data = this.ToDictionary();
            return TableDefinition.GetTableDefinition(typeof(T)).GetScriptDelete(_data);
        }

        public virtual void SetIdFields()
        {
        }

        string IDatabaseObject.GetCreateTableScript()
        {
            return TableDefinition.GetTableDefinition(typeof(T)).GetScriptCreateTable();
        }

        string IDatabaseObject.GetDropTableScript()
        {
            return TableDefinition.GetTableDefinition(typeof(T)).GetScriptDropTable();
        }

//#if !PCL
//        public static IDbConnection GetConnection()
//        {
//            try
//            {
//                var _ret = TableDefinition.GetTableDefinition(typeof(T)).DefaultDataFunction.GetConnection();
//                _ret.Open();

//                return _ret;

//            }
//            catch (Exception ex)
//            {
//                throw new Exception(ex.Message, ex);
//            }
//        }

//#endif

        private void SetSelfColumnsIds(object value)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof (T));
            var _fields = _table.Fields.Select(f => f.Value).Where(f => f.AutomaticValue != AutomaticValue.None);

            foreach (var _field in _fields)
            {
                var _prop = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(f => f.Name == _field.Name);
                if (_prop != null)
                    this.SetObjectFieldValue(_field.Name, _prop.GetValue(value));
            }
#endif
        }

        private static void ThrowException(CrudReturn status)
        {
            if (status.ReturnStatus == CrudStatus.Fail)
                throw new Exception(status.ReturnMessage);
        }

        /// <summary>
        /// Save current changes on object
        /// </summary>
        /// <param name="ignoreAutoIncrementAttribute">if true, doesn't include autoincrement or backend calculated fields on insert statement. Defaults to true</param>
        /// <param name="fireEvent">if true, fire the AfterSaveData event. Defaults to true</param>
        /// <param name="doNotUpdateWhenExists">if true, doesn't fire the update statement when a register already exists in the database. Defaults to false</param>
        /// <param name="validateData"></param>
        /// <returns>true if object is saved on database, otherwise false</returns>
        public bool SaveChanges(bool ignoreAutoIncrementAttribute = true, FireEvent fireEvent = FireEvent.OnBeforeAndAfter, bool doNotUpdateWhenExists = false, bool validateData = false)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof (T));
            var _function = _table.DefaultDataFunction;

            if (validateData)
            {
                if (!this.OnValidateData()) return false;
            }

            if (fireEvent == FireEvent.OnBeforeAndAfter || fireEvent == FireEvent.OnBeforeSave)
                this.OnBeforeSaveData(new NotifyRecordChangesEventArgs(0, SqlStatementsTypes.UnknownStatement));

            var _ret = _function.SaveChanges(this, ignoreAutoIncrementAttribute, doNotUpdateWhenExists);

            ThrowException(_ret);

            if (_ret.ReturnStatus == CrudStatus.Ok && _ret.ChangeType == SqlStatementsTypes.Insert)
            {
                this.SetSelfColumnsIds(_ret.ReturnData);
            }

            if (fireEvent == FireEvent.OnBeforeAndAfter || fireEvent == FireEvent.OnAfterSave)
                this.OnAfterSaveData(new NotifyRecordChangesEventArgs(_ret.RecordsAffected, _ret.ChangeType));

            return _ret.ReturnStatus == CrudStatus.Ok;

#else
            return false;
#endif
        }

        public int Insert(bool ignoreAutoIncrementField = true, bool fireOnAfterSaveData = true, bool validateData = false)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof(T));
            var _function = _table.DefaultDataFunction;

            if (validateData)
            {
                if (!this.OnValidateData()) return 0;
            }

            var _ret = _function.Insert(this, ignoreAutoIncrementField);

            ThrowException(_ret);

            if (_ret.ReturnStatus == CrudStatus.Ok && _ret.ChangeType == SqlStatementsTypes.Insert)
            {
                this.SetSelfColumnsIds(_ret.ReturnData);
            }

            if (fireOnAfterSaveData)
                this.OnAfterSaveData(new NotifyRecordChangesEventArgs(_ret.RecordsAffected, _ret.ChangeType));

            return _ret.RecordsAffected;

#else
            return -1;
#endif
        }

        public int Update(bool fireOnAfterSaveData = true, bool validateData = false)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof(T));
            var _function = _table.DefaultDataFunction;

            if (validateData)
            {
                if (!this.OnValidateData()) return 0;
            }

            var _ret = _function.Update(this);

            ThrowException(_ret);

            if (fireOnAfterSaveData)
                this.OnAfterSaveData(new NotifyRecordChangesEventArgs(_ret.RecordsAffected, _ret.ChangeType));

            return _ret.RecordsAffected;

#else
            return 0;
#endif
        }

        public bool Delete(bool fireBeforeDeleteDataEvent = true, bool fireAfterDeleteDataEvent = true) //, IDbConnection connection = null, bool autoCommit = true)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof(T));
            var _function = _table.DefaultDataFunction;

            if (fireBeforeDeleteDataEvent)
                this.OnBeforeDeleteData(new NotifyRecordChangesEventArgs(0, SqlStatementsTypes.Delete));

            var _ret = _function.Delete(this);

            ThrowException(_ret);

            if (fireAfterDeleteDataEvent)
                this.OnAfterDeleteData(new NotifyRecordChangesEventArgs(_ret.RecordsAffected, _ret.ChangeType));

            return _ret.RecordsAffected > 0;

#else
            return true;
#endif
        }

        public static int DeleteAll(Expression<Func<T, bool>> predicate = null)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof(T));
            var _command = GetDboCommand(predicate, false, SqlStatementsTypes.DeleteAll);
            var _ret = _table.DefaultDataFunction.DeleteAll<T>(_command);

            return _ret.RecordsAffected;
#else
            return -1;
#endif
        }

        public static void Truncate(bool useDropAndCreate = false)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof(T));
            var _function = _table.DefaultDataFunction;

            var _ret = _function.ExecuteStatement(_table.GetScriptDropTable());
            ThrowException(_ret);

            _ret = _function.ExecuteStatement(_table.GetScriptCreateTable());
            ThrowException(_ret);
#endif
        }

        public bool Exists()
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof(T));
            var _sql = _table.GetSqlSelectForCheck();

            return _table.DefaultDataFunction.Exists(_sql, this);
#else
            throw new Exception("Código executado via PCL!");
#endif
        }

        public static bool Exists(Expression<Func<T, bool>> predicate)
        {
#if !PCL
            var _table = TableDefinition.GetTableDefinition(typeof(T));
            var _sql = "select count(0) as tt from " + Name;
            var _sqlCommand = GetCommandSelect(_sql, predicate);

            return _table.DefaultDataFunction.Exists(_sqlCommand);
#else
            throw new Exception("Código executado via PCL!");
#endif
        }

        public void SetObjectFieldValue(string fieldName, object value)
        {
#if !PCL
            var _prop = this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(f => f.Name == fieldName);

            if (_prop != null && _prop.CanWrite)
                _prop.SetValue(this, value, null);
#endif
        }

        protected void OnErrorOnSaveOrDelete(Exception ex)
        {
            if (this.ErrorOnSaveOrDelete != null)
                this.ErrorOnSaveOrDelete(this, new ErrorOnSaveOrDeleteEventArgs(ex));
        }

        private static IEnumerable<FieldDefAttribute> GetObjectFields()
        {
#if !PCL
            return TableDefinition.GetTableDefinition(typeof(T)).Fields.Select(f => f.Value);
#else
            return new List<FieldDefAttribute>();
#endif
        }

        #region "SQL Translator"

        private static DboCommand GetDboCommand(Expression<Func<T, bool>> predicate = null, bool useFieldNames = true, SqlStatementsTypes sqlType = SqlStatementsTypes.Select, object obj  = null)
        {

            var _tableDef = TableDefinition.GetTableDefinition(typeof(T));
            var _selectFields = _tableDef.GetStatementSelect(sqlType == SqlStatementsTypes.SelectReload);

            DboCommand _sqlCommand = null;

            switch (sqlType)
            {
                case SqlStatementsTypes.Select:
                    _sqlCommand = GetCommandSelect(_selectFields, predicate, _tableDef);
                    break;

                case SqlStatementsTypes.DeleteAll:
                    _sqlCommand = GetCommandDelete(predicate);
                    break;

                case SqlStatementsTypes.SelectReload:
                    _sqlCommand = GetCommandSelectReloadMe(_selectFields, obj);
                    break;

                default:
                    throw new NotImplementedException();
            }

            return _sqlCommand;

        }

        public static List<T> Query(string sqlStatement, object dynamicParameters = null) 
        {
#if !PCL
            var _parameters = ToDictionary(dynamicParameters);
            var _ret = TableDefinition.GetTableDefinition(typeof (T)).DefaultDataFunction.Query<T>(sqlStatement, _parameters);
            return _ret;
#else
            throw new Exception("Código executado via PCL!");
#endif
        }

        public static List<T> Query(Expression<Func<T, bool>> predicate = null)
        {

#if !PCL
            const bool useFieldNames = true;
            var _command = GetDboCommand(predicate, useFieldNames);
            var _ret = TableDefinition.GetTableDefinition(typeof(T)).DefaultDataFunction.Query<T>(_command, useFieldNames);
            return _ret;
#else
                throw new Exception("Código executado via PCL!");
#endif
        }

        public T ReloadMe()
        {
#if !PCL
            var _command = GetDboCommand(null, true, SqlStatementsTypes.SelectReload, this);
            var _ret = TableDefinition.GetTableDefinition(typeof(T)).DefaultDataFunction.ReloadMe<T>(_command);
            return _ret;
#else
            return default(T);
#endif
        }

        public static T Load(string sql, object dynamicParameters = null)
        {
            return Query(sql, dynamicParameters).FirstOrDefault();
        }
        
        public static T Load(Expression<Func<T, bool>> predicate)
        {
            return Query(predicate).FirstOrDefault();
        }

        public static string GetStatementTrace(Expression<Func<T, bool>> predicate = null)
        {

#if !PCL
            const bool useFieldNames = true;
            return GetDboCommand(predicate, useFieldNames).GetCommandDefinition().CommandText;
#else
                throw new Exception("Código executado via PCL!");
#endif
        }

        private static DboCommand GetCommandSelect(string selectionList, Expression predicate, TableDefinition tabledef = null)
        {
            var _cmdText = selectionList;

            var _where = predicate;
            var _argNames = new List<string>();
            var _argValues = new List<object>();

            CompileResult _w = null;

            if (_where != null)
                _w = CompileResult.CompileExpr(_where, _argNames, _argValues, TableDefinition.GetTableDefinition(typeof(T)).DefaultDataFunction.PrefixParameter, Name);

            if (_w != null)
            {
                if (tabledef != null)
                    _cmdText += "\r\nwhere " + tabledef.ReplaceTableAlias(_w.CommandText, tabledef);
                else
                    _cmdText += "\r\nwhere " + _w.CommandText;
            }

            return new DboCommand(Name, _cmdText, _argNames.ToArray(), _argValues.ToArray());
        }

        private static DboCommand GetCommandSelectReloadMe(string selectionList, object obj)
        {
            return new DboCommand(Name, selectionList, null, null, obj);
        }

        private static DboCommand GetCommandDelete(Expression predicate)
        {
            var _cmdText = "delete from " + Name + " ";

            var _where = predicate;
            var _argNames = new List<string>();
            var _argValues = new List<object>();

            CompileResult _w = null;

            if (_where != null)
                _w = CompileResult.CompileExpr(_where, _argNames, _argValues, TableDefinition.GetTableDefinition(typeof(T)).DefaultDataFunction.PrefixParameter, Name);

            if (_w != null)
                _cmdText += " where " + _w.CommandText;

            return new DboCommand(Name, _cmdText, _argNames.ToArray(), _argValues.ToArray());
        }

        #endregion
    }

    public interface IDboCommand
    {
        string SqlStatement { get; set; }
        string[] FieldNames { get; set; }
        object[] FieldValues { get; set; }
        object Obj { get; set; }
    }

#if !PCL
    [Serializable]
#endif
    public class DboCommand : IDboCommand
    {
        private string m_ObjectName;
        private string m_SqlStatement;
        private string[] m_FieldNames;
        private object[] m_FieldValues;
        private object m_Obj;

        public DboCommand(string objectName, string sqlStatement, string[] fieldNames, object[] fieldValues, object obj = null)
        {
            m_ObjectName = objectName;
            m_SqlStatement = sqlStatement;
            m_FieldNames = fieldNames;
            m_FieldValues = fieldValues;
            m_Obj = obj;
        }

        public string SqlStatement
        {
            get { return m_SqlStatement; }
            set { m_SqlStatement = value; }
        }

        public string[] FieldNames
        {
            get { return m_FieldNames; }
            set { m_FieldNames = value; }
        }

        public object[] FieldValues
        {
            get { return m_FieldValues; }
            set { m_FieldValues = value; }
        }

        public object Obj
        {
            get { return m_Obj; }
            set { m_Obj = value; }
        }

#if !PCL

        public CommandDefinition GetCommandDefinition()
        {
            return m_Obj == null ? new CommandDefinition(SqlStatement, this.GetDynamicParameters()) : new CommandDefinition(SqlStatement, m_Obj);
        }

        private DynamicParameters GetDynamicParameters()
        {
            if (FieldNames.Length == 0) return null;

            var _parameters = new DynamicParameters();
            for (int _index = 0; _index < FieldNames.Length; _index++)
            {
                _parameters.Add(FieldNames[_index].Replace(m_ObjectName + ".", ""), FieldValues[_index]);
            }
            return _parameters;
        }
#endif
    }
}