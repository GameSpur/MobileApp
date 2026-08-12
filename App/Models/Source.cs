using Newtonsoft.Json;
using SQLite;
using SQLiteNetExtensions.Attributes;
using System.Collections.ObjectModel;

namespace GamHubApp.Models;

public class Source : SelectableModel
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Column("mongoId"), JsonProperty("_id")]
    public string MongoId { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("url")]
    public string Url { get; set; }
    [JsonProperty("primaryColour")]
    public string PrimaryColour { get; set; }
    [JsonProperty("secondaryColour")]
    public string SecondaryColour { get; set; }
    [JsonProperty("domain")]
    public string Domain { get; set; }
    [JsonProperty("logo")]
    public string Logo { get; set; }
    [JsonProperty("isActive")]
    public bool IsActive { get; set; }
    [JsonIgnore]
    [OneToMany(CascadeOperations = CascadeOperation.All)]
    public Collection<Article> RelatedArticles { get; set; }

    public Source()
    {
    }

}
