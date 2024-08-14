namespace Auth.Api.Database.Models;

public class Function
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public Guid? ParrentId { get; set; }
    public int? SortOrder { get; set; }
    public string CssClass { get; set; }
    public bool? IsActive { get; set; }
    public virtual Function? Parent {  get; set; }
    public virtual ICollection<Function>? Functions { get; set; }
    public virtual ICollection<Permission>? Permissions { get; set; }
    public virtual ICollection<ActionInFunction>? ActionInFunctions { get; set; }
}
