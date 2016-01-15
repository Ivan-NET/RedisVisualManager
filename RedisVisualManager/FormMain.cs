using ServiceStack.Redis;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RedisVisualManager
{
    public partial class FormMain : Form
    {
        public static IRedisClient RedisClient;
        private static string INI_FILE = "host.ini";
        DataTable table = null;

        public FormMain()
        {
            InitializeComponent();

            ReadFromIniFile();

            gridHash.AutoGenerateColumns = false;
            table = new DataTable();
            table.Columns.Add("row");
            table.Columns.Add("key");
            table.Columns.Add("value");
            gridHash.DataSource = table;
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (DoConnection())
                MessageBox.Show("Connect Success!", "", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool DoConnection()
        {
            try
            {
                if (RedisClient != null)
                {
                    RedisClient.Dispose();
                    RedisClient = null;
                }
                RedisClient = new RedisClient(txtHost.Text, Convert.ToInt32(txtPort.Text), txtAuth.Text);
                int timeout = Convert.ToInt32(txtTimeout.Text);
                RedisClient.ConnectTimeout = RedisClient.SendTimeout = RedisClient.RetryTimeout = timeout;
                RedisClient.GetValue("foo");
                WriteToIniFile();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connect Failed!" + Environment.NewLine + ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        private void ReadFromIniFile()
        {
            if (File.Exists(INI_FILE))
            {
                using (StreamReader sr = new StreamReader(INI_FILE))
                {
                    string str = sr.ReadLine();
                    string[] strs = str.Split(',');
                    if (strs.Length == 4)
                    {
                        txtHost.Text = strs[0];
                        txtPort.Text = strs[1];
                        txtAuth.Text = strs[2];
                        txtTimeout.Text = strs[3];
                    }
                }
            }
        }

        private void WriteToIniFile()
        {
            using (StreamWriter sw = new StreamWriter(INI_FILE, false))
            {         
                sw.Write(string.Format("{0},{1},{2},{3}", txtHost.Text, txtPort.Text, txtAuth.Text, txtTimeout.Text));
            }
        }

        private void btnSearchKey_Click(object sender, EventArgs e)
        {
            if (RedisClient == null)
                DoConnection();
            try
            {
                this.treeKeys.Nodes.Clear();
                System.Threading.EventWaitHandle waitHandle = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset);
                List<string> keys = null;
                new Task(() =>
                {
                    keys = RedisClient.SearchKeys(txtPattern.Text);
                    waitHandle.Set();
                }).Start();
                if (!waitHandle.WaitOne(RedisClient.SendTimeout))
                {
                    RedisClient.Dispose();
                    RedisClient = null;
                    throw new Exception("search keys timeout!");
                }
                NestedHash keysTree = new NestedHash();
                foreach (string key in keys)
                {
                    string[] keySegments = key.Split(':');
                    NestedHash pHash = keysTree;　// 指针
                    string pKeySegment = "";　// 指针
                    for (int i = 0; i < keySegments.Length; i++)
                    {
                        string keySegment = keySegments[i];
                        if (i > 0)
                        {
                            if (pHash[pKeySegment] == null)
                            {
                                NestedHash hash = new NestedHash();
                                pHash[pKeySegment] = hash;
                                pHash = hash;
                            }
                            else
                            {
                                pHash = pHash[pKeySegment] as NestedHash;
                            }
                        }

                        if (!pHash.ContainsKey(keySegment))
                            pHash.Add(keySegment, null);

                        pKeySegment = keySegment;
                    }
                }
                BuildTree(this.treeKeys.Nodes, keysTree);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BuildTree(TreeNodeCollection treeNodes, NestedHash nestedHash)
        {
            foreach (var item in nestedHash)
            {
                TreeNode treeNode = treeNodes.Add(item.Key);
                NestedHash hash = item.Value as NestedHash;
                if (hash != null)
                {
                    BuildTree(treeNode.Nodes, hash);
                }
            }
        }

        private void treeKeys_AfterSelect(object sender, TreeViewEventArgs e)
        {
            bool isKey = e.Node.Nodes.Count == 0;
            if (isKey)
            {
                this.table.Clear();
                this.txtHashKey.Text = this.txtHashValue.Text = string.Empty;

                string key = GetFullKey(e.Node);
                RedisKeyType keyType = RedisClient.GetEntryType(key);
                this.lblKey.Text = keyType.ToString();
                this.txtKey.Text = key;
                int row = 0;
                switch (keyType)
                {
                    case RedisKeyType.Hash:
                        this.gridHash.Visible = true;
                        this.column_key.Visible = this.column_value.Visible = true;
                        var hash = RedisClient.GetAllEntriesFromHash(key);
                        foreach (var item in hash)
                        {
                            table.Rows.Add(row + 1, item.Key, item.Value);
                            row++;
                        }
                        break;
                    case RedisKeyType.List:
                        this.gridHash.Visible = true;
                        this.column_key.Visible = false;
                        this.column_value.Visible = true;
                        var list = RedisClient.GetAllItemsFromList(key);
                        for (; row < list.Count; row++)
                        {
                            table.Rows.Add(row + 1, "", list[row]);
                        }
                        break;
                    case RedisKeyType.None:
                        break;
                    case RedisKeyType.Set:
                        this.gridHash.Visible = true;
                        this.column_key.Visible = true;
                        this.column_value.Visible = false;
                        var set = RedisClient.GetAllItemsFromSet(key);
                        foreach (var item in set)
                        {
                            table.Rows.Add(row + 1, item, "");
                            row++;
                        }
                        break;
                    case RedisKeyType.SortedSet:
                        this.gridHash.Visible = true;
                        var sortedSet = RedisClient.GetAllWithScoresFromSortedSet(key);
                        foreach (var item in sortedSet)
                        {
                            table.Rows.Add(row + 1, item.Key, item.Value);
                            row++;
                        }
                        break;
                    case RedisKeyType.String:
                        this.txtHashValue.Text = RedisClient.GetValue(key);
                        break;
                    default:
                        break;
                }
            }
        }

        private string GetFullKey(TreeNode node)
        {
            string key = node.Text;
            while (node.Parent != null)
            {
                key = node.Parent.Text + ":" + key;
                node = node.Parent;
            }
            return key;
        }

        private void gridHash_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            var row = this.gridHash.Rows[e.RowIndex];
            this.txtHashKey.Text = Convert.ToString(row.Cells["column_key"].Value);
            this.txtHashValue.Text = Convert.ToString(row.Cells["column_value"].Value);
        }
    }

    class NestedHash : Dictionary<string, object>
    {
    }
}
