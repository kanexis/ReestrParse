using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReestrParse.WebBot.Interface
{
    public interface IParserComboBox
    {
        void LoadDataAsync(ComboBox combo);
        string GetSelectAsync(string url);
    }
}
