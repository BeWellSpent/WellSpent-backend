using AutoMapper;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Notifications;

public sealed class NotificationMappingProfile : Profile
{
    public NotificationMappingProfile()
    {
        CreateMap<Notification, NotificationDto>();
        CreateMap<AlertSubscription, AlertSubscriptionDto>();
    }
}
