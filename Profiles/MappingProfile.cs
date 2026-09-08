using System;
using System.Linq;
using System.Collections.Generic;
using AutoMapper;
using Inventory.Msv.Models;
using MessageMQCommon.MQ.Messages.OrderMsv;
using MessageMQCommon.MQ.Messages.PurchaseMsv;

namespace Inventory.Msv.Profiles
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Map individual order detail -> stock mutation (one mutation per product in the order)
            CreateMap<OrderMessage, TrxStockMutation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.ProductId, opt => opt.MapFrom(src => src.TrxOrdersDetails.Select(d => d.ProductId)))
                .ForMember(dest => dest.ReferenceType, opt => opt.MapFrom(src => src.OrderNumber))
                .ForMember(dest => dest.ReferenceId, opt => opt.MapFrom(src => src.TrxOrdersDetails.Select(d => d.Id)))
                .ForMember(dest => dest.QtyIn, opt => opt.MapFrom(_ => 0))
                .ForMember(dest => dest.QtyOut, opt => opt.MapFrom(src => src.TrxOrdersDetails.Sum(d => d.Quantity)))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt ?? DateTime.UtcNow));

            CreateMap<OrderDetailMessage, TrxStockMutation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.ProductId, opt => opt.MapFrom(src => src.ProductId))
                .ForMember(dest => dest.ReferenceType, opt => opt.MapFrom(_ => "ORDER"))
                .ForMember(dest => dest.ReferenceId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.QtyIn, opt => opt.MapFrom(_ => 0))
                .ForMember(dest => dest.QtyOut, opt => opt.MapFrom(src => src.Quantity));

            CreateMap<PurchaseDetailMessage,  TrxStockMutation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.ProductId, opt => opt.MapFrom(src => src.ProductId))
                .ForMember(dest => dest.ReferenceType, opt => opt.MapFrom(_ => "ORDER"))
                .ForMember(dest => dest.ReferenceId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.QtyIn, opt => opt.MapFrom(src => src.Quantity))
                .ForMember(dest => dest.QtyOut, opt => opt.MapFrom(_ => 0));
        }
    }
}
