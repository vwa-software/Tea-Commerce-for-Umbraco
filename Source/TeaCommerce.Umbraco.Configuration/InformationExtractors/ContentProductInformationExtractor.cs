using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.XPath;
using Autofac;
using System;
using System.Linq;
using System.Xml.Linq;
using TeaCommerce.Api.Common;
using TeaCommerce.Api.Dependency;
using TeaCommerce.Api.Models;
using TeaCommerce.Api.Services;
using TeaCommerce.Umbraco.Configuration.Services;
using TeaCommerce.Umbraco.Configuration.Variant.Product;
using umbraco;
using Umbraco.Core;
using Umbraco.Core.Dynamics;
using Umbraco.Core.Models;
using Umbraco.Web;
using Constants = TeaCommerce.Api.Constants;
using TeaCommerce.Api.InformationExtractors;
using TeaCommerce.Umbraco.Configuration.Variant;

namespace TeaCommerce.Umbraco.Configuration.InformationExtractors {

  public class ContentProductInformationExtractor : IProductInformationExtractor<IContent, VariantPublishedContent<IContent>> {

    protected readonly IStoreService StoreService;
    protected readonly ICurrencyService CurrencyService;
    protected readonly IVatGroupService VatGroupService;
    protected readonly IVariantService<IContent> VariantService;

    public static IProductInformationExtractor<IContent, VariantPublishedContent<IContent>> Instance { get { return DependencyContainer.Instance.Resolve<IProductInformationExtractor<IContent, VariantPublishedContent<IContent>>>(); } }

    public ContentProductInformationExtractor( IStoreService storeService, ICurrencyService currencyService, IVatGroupService vatGroupService, IVariantService<IContent> variantService ) {
      StoreService = storeService;
      CurrencyService = currencyService;
      VatGroupService = vatGroupService;
      VariantService = variantService;
    }

    public virtual T GetPropertyValue<T>( IContent model, string propertyAlias, VariantPublishedContent<IContent> variant = null, Func<IContent, bool> func = null ) {
      T rtnValue = default( T );

      if ( model != null && !string.IsNullOrEmpty( propertyAlias ) ) {
        if ( variant != null ) {
          rtnValue = variant.GetPropertyValue<T>( propertyAlias );
        }
        if ( CheckNullOrEmpty( rtnValue ) ) {
          //Check if this node or ancestor has it
          IContent currentNode = func != null ? model.Ancestors().FirstOrDefault( func ) : model;
          if ( currentNode != null ) {
            rtnValue = GetPropertyValueInternal<T>( currentNode, propertyAlias, func == null );
          }

          //Check if we found the value
          if ( CheckNullOrEmpty( rtnValue ) ) {

            //Check if we can find a master relation
            string masterRelationNodeIdStr = GetPropertyValueInternal<string>( model, Constants.ProductPropertyAliases.MasterRelationPropertyAlias, true );
            int masterRelationNodeId = 0;
            if ( !string.IsNullOrEmpty( masterRelationNodeIdStr ) && int.TryParse( masterRelationNodeIdStr, out masterRelationNodeId ) ) {
              rtnValue = GetPropertyValue<T>( ApplicationContext.Current.Services.ContentService.GetById( masterRelationNodeId ), propertyAlias,
                variant, func );
            }
          }

        }
      }

      return rtnValue;
    }

    protected virtual T GetPropertyValueInternal<T>( IContent content, string propertyAlias, bool recursive ) {
      T rtnValue = default( T );

      if ( content != null && !string.IsNullOrEmpty( propertyAlias ) ) {

        if ( !recursive ) {
          rtnValue = content.GetValue<T>( propertyAlias );
        } else {
          //We need to go recursive
          IContent tempModel = content;
          T tempProperty = default( T );
          try {
            tempProperty = tempModel.GetValue<T>( propertyAlias );
          } catch { }
          if ( !CheckNullOrEmpty( tempProperty ) ) {
            rtnValue = tempProperty;
          }

          while ( CheckNullOrEmpty( rtnValue ) && tempModel != null && tempModel.Id > 0 ) {
            tempModel = tempModel.Parent();
            if ( tempModel != null ) {
              try {
                tempProperty = tempModel.GetValue<T>( propertyAlias );
              } catch { }
              if ( !CheckNullOrEmpty( tempProperty ) ) {
                rtnValue = tempProperty;
              }
            }
          }
        }
      }

      return rtnValue;
    }

    public virtual long GetStoreId( IContent model ) {
      long? storeId = GetPropertyValue<long?>( model, Constants.ProductPropertyAliases.StorePropertyAlias );
      if ( storeId == null ) {
        throw new ArgumentException( "The model doesn't have a store id associated with it - remember to add the Tea Commerce store picker to your Umbraco content tree" );
      }

      return storeId.Value;
    }

    public virtual string GetSku( IContent model, VariantPublishedContent<IContent> variant = null ) {
      string sku = GetPropertyValue<string>( model, Constants.ProductPropertyAliases.SkuPropertyAlias, variant );

      //If no sku is found - default to umbraco node id
      if ( string.IsNullOrEmpty( sku ) ) {
        sku = model.Id.ToString( CultureInfo.InvariantCulture ) + "_" + variant.VariantId;
      }

      return sku;
    }

    public virtual long? GetVatGroupId( IContent model, VariantPublishedContent<IContent> variant = null ) {
      long storeId = GetStoreId( model );
      long? vatGroupId = GetPropertyValue<long?>( model, Constants.ProductPropertyAliases.VatGroupPropertyAlias, variant );

      //In umbraco a product can have a relation to a delete marked vat group
      if ( vatGroupId != null ) {
        VatGroup vatGroup = VatGroupService.Get( storeId, vatGroupId.Value );
        if ( vatGroup == null || vatGroup.IsDeleted ) {
          vatGroupId = null;
        }
      }

      return vatGroupId;
    }

    public virtual long? GetLanguageId( IContent model ) {
      return LanguageService.Instance.GetLanguageIdByNodePath( model.Path );
    }

    private static bool CheckNullOrEmpty<T>( T value ) {
      if ( typeof( T ) == typeof( string ) )
        return string.IsNullOrEmpty( value as string );

      return value == null || value.Equals( default( T ) );
    }

    public string GetPropertyValue( IContent product, VariantPublishedContent<IContent> variant, string propertyAlias ) {
      return GetPropertyValue<string>( product, Constants.ProductPropertyAliases.NamePropertyAlias, variant );
    }

    public string GetName( IContent product, VariantPublishedContent<IContent> variant ) {
      string name = GetPropertyValue<string>( product, Constants.ProductPropertyAliases.NamePropertyAlias, variant );

      //If no name is found - default to the umbraco node name
      if ( string.IsNullOrEmpty( name ) ) {
        if ( variant != null ) {
          name = GetPropertyValue<string>( product, Constants.ProductPropertyAliases.NamePropertyAlias );
        }
        if ( string.IsNullOrEmpty( name ) ) {
          name = product.Name;
        }
        if ( variant != null ) {
          name += " - " + variant.Name;
        }
      }

      return name;
    }

    public OriginalUnitPriceCollection GetOriginalUnitPrices( IContent product, VariantPublishedContent<IContent> variant ) {
      OriginalUnitPriceCollection prices = new OriginalUnitPriceCollection();

      foreach ( Currency currency in CurrencyService.GetAll( GetStoreId( product ) ) ) {
        prices.Add( new OriginalUnitPrice( GetPropertyValue<string>( product, currency.PricePropertyAlias, variant ).ParseToDecimal() ?? 0M, currency.Id ) );
      }

      return prices;
    }

    public CustomPropertyCollection GetProperties( IContent product, VariantPublishedContent<IContent> variant ) {
      CustomPropertyCollection properties = new CustomPropertyCollection();

      foreach ( string productPropertyAlias in StoreService.Get( GetStoreId( product ) ).ProductSettings.ProductPropertyAliases ) {
        properties.Add( new CustomProperty( productPropertyAlias, GetPropertyValue<string>( product, productPropertyAlias, variant ) ) { IsReadOnly = true } );
      }

      return properties;
    }
  }
}
