resource "namecheap_domain_records" "adminweb" {
  domain = var.adminweb_dns_zone
  mode   = "MERGE"

  record {
    address  = local.adminweb_default_fqdn
    hostname = local.adminweb_custom_hostname
    ttl      = 300
    type     = "CNAME"
  }

  record {
    address  = azurerm_container_app.adminweb.custom_domain_verification_id
    hostname = "asuid.${local.adminweb_custom_hostname}"
    ttl      = 300
    type     = "TXT"
  }
}

resource "azurerm_container_app_custom_domain" "adminweb" {
  name             = local.adminweb_custom_fqdn
  container_app_id = azurerm_container_app.adminweb.id

  depends_on = [namecheap_domain_records.adminweb]

  lifecycle {
    # Azure fills these asynchronously when it provisions the managed certificate.
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }

  timeouts {
    create = "60m"
  }
}
