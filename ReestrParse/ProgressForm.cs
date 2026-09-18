using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ReestrParse
{
    public partial class ProgressForm : Form
    {
        public ProgressForm()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterScreen; // Центрируем форму
            this.TopMost = true;
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }
        public void UpdateProgress(int value)
        {
            if (progressBar1.InvokeRequired)
            {
                progressBar1.Invoke(new Action<int>(UpdateProgress), value);
            }
            else
            {
                progressBar1.Value = value;
            }
        }


        private void progressBar1_Click(object sender, EventArgs e)
        {

        }

        private void ProgressForm_Load(object sender, EventArgs e)
        {

        }
    }
}
