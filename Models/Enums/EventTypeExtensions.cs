using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace StanTrack.Models.Enums
{
    public static class EventTypeExtensions
    {
        public static string GetDisplayName(this EventType value)
        {
            var member = typeof(EventType).GetMember(value.ToString());
            if (member.Length == 0) return value.ToString();
            var display = member[0].GetCustomAttribute<DisplayAttribute>();
            return display?.GetName() ?? value.ToString();
        }
    }
}
