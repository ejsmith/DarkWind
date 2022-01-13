using System.Text.Json.Serialization;

namespace DarkWind.Shared;

public class CharEnemy 
{

    [JsonPropertyName("enemy_name")]
    public string EnemyName { get; set; }

    [JsonPropertyName("enemy_maxsp")]
    public int EnemyMaxSpellpoints { get; set; }

    [JsonPropertyName("enemy_cursp")]
    public int EnemyCurrentSpellpoints { get; set; }

    [JsonPropertyName("enemy_curhp")]
    public int EnemyCurrentHealth { get; set; }

    [JsonPropertyName("enemy_maxhp")]
    public int EnemyMaxHealth { get; set; }

    [JsonPropertyName("enemy_hp_string")]
    public string EnemyHealthDescription { get; set; }

}
