using Oracle.ManagedDataAccess.Client;
using System;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using MahApps.Metro.Controls;
using System.Windows.Forms;

namespace EntityGenerator
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 测试数据库连接按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnTestConnect_Click(object sender, RoutedEventArgs e)
        {
            OracleConnection oracleConn = null;
            try
            {
                // 获取连接字符串
                var connString = this.connectionString.Text;
                using (oracleConn = new OracleConnection(connString))
                {

                    oracleConn.Open();
                    System.Windows.MessageBox.Show("连接成功！现在可以生成实体了。");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"连接失败！错误信息：{ex.Message}");
            }
            finally
            {
                if (oracleConn != null)
                    oracleConn.Close();
            }
        }


        /// <summary>
        /// 生成实体类按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            // 获取连接字符串
            var connString = this.connectionString.Text;
            // 获取用户输入的表名
            var tableName = this.tableName.Text.Trim();

            using (var oracleConn = new OracleConnection(connString))
            {
                try
                {
                    oracleConn.Open();
                    using (var cmd = oracleConn.CreateCommand())
                    {
                        OracleParameter[] parameter = {
                            new OracleParameter(":TABLE_NAME", OracleDbType.NVarchar2, 36)
                        };
                        parameter[0].Value = tableName;
                        cmd.Parameters.AddRange(parameter);

                        // 查找表中的字段
                        var sql = @"SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, NULLABLE FROM USER_TAB_COLUMNS T WHERE T.TABLE_NAME = :TABLE_NAME";
                        cmd.CommandText = sql;
                        OracleDataAdapter adapter = new OracleDataAdapter(cmd);
                        DataTable fieldsDataTable = new DataTable();
                        adapter.Fill(fieldsDataTable);

                        // 查找表中字段的注释
                        var sql2 = @"SELECT COLUMN_NAME, COMMENTS FROM USER_COL_COMMENTS T WHERE T.TABLE_NAME = :TABLE_NAME";
                        cmd.CommandText = sql2;
                        adapter = new OracleDataAdapter(cmd);
                        DataTable commentsDataTable = new DataTable();
                        adapter.Fill(commentsDataTable);

                        // 合并两张表，生成一个包含字段信息的表格
                        DataTable result = CombineTwoDataTables(fieldsDataTable, commentsDataTable);

                        AnalysisFieldsInfo(result);
                        System.Windows.MessageBox.Show("生成成功！");
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"实体生成失败！错误信息：{ex.Message}");
                }
                finally
                {
                    oracleConn.Close();
                }
            }
        }

        /// <summary>
        /// 将字段表和字段注释表合并为一个 DataTable
        /// </summary>
        /// <param name="dataTable"></param>
        /// <param name="otherDataTable"></param>
        /// <returns></returns>
        private DataTable CombineTwoDataTables(DataTable dataTable, DataTable otherDataTable)
        {
            DataTable newDataTable = new DataTable();
            newDataTable.Columns.Add("COLUMN_NAME", typeof(string));
            newDataTable.Columns.Add("DATA_TYPE", typeof(string));
            newDataTable.Columns.Add("DATA_LENGTH", typeof(int));
            newDataTable.Columns.Add("DATA_PRECISION", typeof(int));
            newDataTable.Columns.Add("NULLABLE", typeof(string));
            newDataTable.Columns.Add("COMMENTS", typeof(string));

            object[] obj = new object[newDataTable.Columns.Count];

            // 添加 dataTable 的数据
            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                dataTable.Rows[i].ItemArray.CopyTo(obj, 0);
                obj[5] = otherDataTable.Rows[i].ItemArray[1];
                newDataTable.Rows.Add(obj);
            }
            return newDataTable;
        }

        /// <summary>
        /// 分析每个字段的信息，并生成类文件
        /// </summary>
        /// <param name="dataTable">包含字段信息的表格</param>
        private void AnalysisFieldsInfo(DataTable dataTable)
        {
            // 先将类名写好
            var tableName = this.tableName.Text.Trim();
            StringBuilder classContent = new StringBuilder();
            var content = "public class " + tableName + "\n{\n";
            classContent.Append(content);

            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                // 字段名英文称
                var columnName = dataTable.Rows[i]["COLUMN_NAME"].ToString();
                // 字段类型
                var dataType = dataTable.Rows[i]["DATA_TYPE"].ToString();
                // 字段长度
                var dataLength = int.Parse(dataTable.Rows[i]["DATA_LENGTH"].ToString());
                // 字段精度
                var dataPrecision = !string.IsNullOrEmpty(dataTable.Rows[i]["DATA_PRECISION"].ToString()) ? int.Parse(dataTable.Rows[i]["DATA_PRECISION"].ToString()) : 0;
                // 字段是否可为空
                var nullable = dataTable.Rows[i]["NULLABLE"].ToString();
                // 字段注释信息
                var comments = dataTable.Rows[i]["COMMENTS"].ToString();

                classContent.Append(GenerateEntityClass(columnName, dataType, dataPrecision, nullable, comments));

                // 如果是最后一个属性，需要在后面添加一个大括号，将类包裹起来
                if (i == dataTable.Rows.Count - 1)
                {
                    classContent.Append("}");
                }
            }
            if (!Directory.Exists(this.filePath.Text))
            {
                throw new Exception("路径不存在！");
            }
            FileStream fs = new FileStream(this.filePath.Text + "\\" + tableName + ".cs", FileMode.Create);
            using (StreamWriter sw = new StreamWriter(fs))
            {
                sw.Write(classContent.ToString());

                sw.Flush();
                sw.Close();
            }
            fs.Close();
        }

        /// <summary>
        /// 生成实体类
        /// </summary>
        /// <param name="columnName"></param>
        /// <param name="dataType"></param>
        /// <param name="nullable"></param>
        /// <param name="comments"></param>
        private StringBuilder GenerateEntityClass(string columnName, string dataType, int dataPrecision, string nullable, string comments)
        {
            var tableName = this.tableName.Text.Trim();
            StringBuilder classContent = new StringBuilder();
            string comment = "    /// <summary>\n" +
                             "    /// " + comments + "\n" +
                             "    /// </summary>\n";
            classContent.Append(comment);

            // 组装类型
            dataType = DealDataType(dataType, dataPrecision, nullable);

            classContent.Append("    public " + dataType + " " + columnName + " { get; set; }\n");

            return classContent;
        }


        /// <summary>
        /// 根据字段类型、字段精度、字段是否可为空，组装属性的类型
        /// </summary>
        /// <param name="dataType">字段类型</param>
        /// <param name="dataPrecision">字段精度</param>
        /// <param name="nullable">字段是否可为空（值域：Y-是；N-否）</param>
        /// <returns></returns>
        private string DealDataType(string dataType, int dataPrecision, string nullable)
        {
            // 组装类型
            string type = string.Empty;
            dataType = dataType.ToUpper();
            if (dataType.Contains("VARCHAR2"))
            {
                type = "string";
            }
            // 如果是数值类型，还要区分是否存在精度的限制
            else if (dataType.Equals("NUMBER"))
            {
                // 如果存在精度的限制，则类型设为 decimal
                if (string.IsNullOrEmpty(dataPrecision.ToString()) || dataPrecision == 0)
                {
                    type = "int";
                }
                // 如果没有精度的限制，则将类型设置为 int
                else
                {
                    type = "decimal";
                }
                // 另外如果数值类型可为空，则需要设为 nullable 类型
                if (nullable.Equals("Y"))
                {
                    type += "?";
                }
            }
            else if (dataType.Equals("DATE"))
            {
                type = "DateTime";
                // 日期类型如果可为空，则需要设为 nullable 类型
                if (nullable.Equals("Y"))
                {
                    type += "?";
                }
            }
            return type;
        }


        /// <summary>
        /// 选择文件夹按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnSelectPath_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.ShowDialog();
            if (!string.IsNullOrEmpty(dialog.SelectedPath))
            {
                this.filePath.Text = dialog.SelectedPath;
            }
        }
    }
}
