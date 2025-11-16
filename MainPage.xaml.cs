using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using System.Linq;
using Microsoft.Maui.Controls;



namespace labas
{  public partial class MainPage : ContentPage

    {
        private string? _xmlPath;
        private string? _xsltPath;
        private AnalysisContext _analysisContext;

        public MainPage()
        {
            InitializeComponent();
            _analysisContext = new AnalysisContext();
        }

        private async void OnExitClicked(object sender, EventArgs e)
        {
            bool answer = await DisplayAlert("Exit", "Чи дійсно ви хочете завершити роботу з програмою?", "Так", "Ні");
            if (answer)
            {
                Application.Current.Quit();
            }
        }

        private async void OnLoadXmlClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select students.xml",
                });

                if (result != null)
                {
                    _xmlPath = result.FullPath;
                    LblXmlPath.Text = result.FileName;
                    PopulateFacultyPicker();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load XML: {ex.Message}", "OK");
            }
        }

        private void PopulateFacultyPicker()
        {
            if (string.IsNullOrEmpty(_xmlPath)) return;
            try
            {
                var doc = XDocument.Load(_xmlPath);
                var faculties = doc.Descendants("Student")
                                   .Select(s => (string?)s.Attribute("Faculty"))
                                   .Where(f => !string.IsNullOrEmpty(f))
                                   .Distinct()
                                   .OrderBy(f => f)
                                   .ToList();

                faculties.Insert(0, "All");
                PickerFaculty.ItemsSource = faculties;
                PickerFaculty.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                DisplayAlert("Error", $"Failed to parse faculties: {ex.Message}", "OK");
            }
        }

        private async void OnLoadXsltClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select students.xsl",
                });

                if (result != null)
                {
                    _xsltPath = result.FullPath;
                    LblXsltPath.Text = result.FileName;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load XSLT: {ex.Message}", "OK");
            }
        }

        private void OnAnalyzeClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_xmlPath))
            {
                DisplayAlert("Error", "Load XML file first.", "OK");
                return;
            }

            string? selectedFaculty = PickerFaculty.SelectedItem as string;
            if (selectedFaculty == "All") selectedFaculty = null;
            string department = EntryDepartment.Text;

            string selectedStrategy = PickerStrategy.SelectedItem?.ToString() ?? "DOM";

            switch (selectedStrategy)
            {
                case "DOM":
                    _analysisContext.SetStrategy(new DomAnalysisStrategy());
                    break;
                case "SAX":
                    _analysisContext.SetStrategy(new SaxAnalysisStrategy());
                    break;
                case "LINQ":
                    _analysisContext.SetStrategy(new LinqAnalysisStrategy());
                    break;
            }

            try
            {
                var results = _analysisContext.ExecuteStrategy(_xmlPath, selectedFaculty, department);
                EditorResults.Text = string.Join(Environment.NewLine, results);
            }
            catch (Exception ex)
            {
                EditorResults.Text = $"Analysis failed: {ex.Message}";
            }
        }

        private async void OnTransformClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_xmlPath) || string.IsNullOrEmpty(_xsltPath))
            {
                await DisplayAlert("Error", "Load XML and XSLT files first.", "OK");
                return;
            }

            try
            {
                string outputHtmlPath = Path.Combine(FileSystem.CacheDirectory, "students_transform.html");

                var xslt = new XslCompiledTransform();
                xslt.Load(_xsltPath);
                xslt.Transform(_xmlPath, outputHtmlPath);

                EditorResults.Text = $"Transformation successful. Opening file...";

                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(outputHtmlPath)
                });
            }
            catch (Exception ex)
            {
                EditorResults.Text = $"Transform failed: {ex.Message}";
            }
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            EntryDepartment.Text = string.Empty;
            if (PickerFaculty.ItemsSource != null && PickerFaculty.ItemsSource.Count > 0)
            {
                PickerFaculty.SelectedIndex = 0;
            }
            PickerStrategy.SelectedIndex = 0;
            EditorResults.Text = string.Empty;
        }
    }

    public interface IAnalysisStrategy
    {
        List<string> Analyze(string xmlPath, string? faculty, string? department);
    }

    public class DomAnalysisStrategy : IAnalysisStrategy
    {
        public List<string> Analyze(string xmlPath, string? faculty, string? department)
        {
            var results = new List<string>();
            var doc = new XmlDocument();
            doc.Load(xmlPath);

            string xpath = "/University/Student";
            var conditions = new List<string>();
            if (!string.IsNullOrEmpty(faculty)) conditions.Add($"@Faculty='{faculty}'");
            if (!string.IsNullOrEmpty(department)) conditions.Add($"@Department='{department}'");

            if (conditions.Count > 0)
            {
                xpath += $"[{string.Join(" and ", conditions)}]";
            }

            var nodes = doc.SelectNodes(xpath);
            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    var nameNode = node.SelectSingleNode("Name");
                    if (nameNode != null)
                    {
                        results.Add(nameNode.InnerText);
                    }
                }
            }
            return results;
        }
    }

    public class SaxAnalysisStrategy : IAnalysisStrategy
    {
        public List<string> Analyze(string xmlPath, string? faculty, string? department)
        {
            var results = new List<string>();
            using (var reader = XmlReader.Create(xmlPath))
            {
                string? currentStudentName = null;
                bool facultyMatch = string.IsNullOrEmpty(faculty);
                bool departmentMatch = string.IsNullOrEmpty(department);
                bool inStudent = false;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "Student")
                    {
                        inStudent = true;
                        string? attrFaculty = reader.GetAttribute("Faculty");
                        string? attrDepartment = reader.GetAttribute("Department");

                        facultyMatch = string.IsNullOrEmpty(faculty) || (attrFaculty == faculty);
                        departmentMatch = string.IsNullOrEmpty(department) || (attrDepartment == department);
                    }

                    if (inStudent && reader.NodeType == XmlNodeType.Element && reader.Name == "Name")
                    {
                        if (reader.Read())
                        {
                            currentStudentName = reader.Value;
                        }
                    }

                    if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "Student")
                    {
                        if (facultyMatch && departmentMatch && currentStudentName != null)
                        {
                            results.Add(currentStudentName);
                        }
                        inStudent = false;
                        currentStudentName = null;
                    }
                }
            }
            return results;
        }
    }

    public class LinqAnalysisStrategy : IAnalysisStrategy
    {
        public List<string> Analyze(string xmlPath, string? faculty, string? department)
        {
            var doc = XDocument.Load(xmlPath);
            var query = doc.Descendants("Student");

            if (!string.IsNullOrEmpty(faculty))
            {
                query = query.Where(s => (string?)s.Attribute("Faculty") == faculty);
            }
            if (!string.IsNullOrEmpty(department))
            {
                query = query.Where(s => (string?)s.Attribute("Department") == department);
            }

            return query.Select(s => s.Element("Name")?.Value ?? string.Empty).ToList();
        }
    }

    public class AnalysisContext
    {
        private IAnalysisStrategy? _strategy;

        public void SetStrategy(IAnalysisStrategy strategy)
        {
            _strategy = strategy;
        }

        public List<string> ExecuteStrategy(string xmlPath, string? faculty, string? department)
        {
            if (_strategy == null)
            {
                return new List<string> { "Error: Strategy not set." };
            }
            return _strategy.Analyze(xmlPath, faculty, department);
        }
    }
}