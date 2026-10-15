// WPF/WinForms 项目的隐式 using 里没有 System.IO 这类常用命名空间，这里显式补上，
// 同时避免把 System.Windows.Forms / System.Drawing 引进来造成类型冲突。
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using DeskNest.App.Ui;
global using DeskNest.App.Host;
global using DeskNest.Native.Win32;
