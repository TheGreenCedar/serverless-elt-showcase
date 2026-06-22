resource "aws_api_gateway_rest_api" "read_api" {
  name = "${var.project_name}-read-api"
}

resource "aws_api_gateway_resource" "fuel_mix" {
  parent_id   = aws_api_gateway_rest_api.read_api.root_resource_id
  path_part   = "fuel-mix"
  rest_api_id = aws_api_gateway_rest_api.read_api.id
}

resource "aws_api_gateway_resource" "latest" {
  parent_id   = aws_api_gateway_resource.fuel_mix.id
  path_part   = "latest"
  rest_api_id = aws_api_gateway_rest_api.read_api.id
}

resource "aws_api_gateway_method" "latest_get" {
  api_key_required = true
  authorization    = "NONE"
  http_method      = "GET"
  resource_id      = aws_api_gateway_resource.latest.id
  rest_api_id      = aws_api_gateway_rest_api.read_api.id

  request_parameters = {
    "method.request.querystring.category" = false
    "method.request.querystring.from"     = false
    "method.request.querystring.limit"    = false
    "method.request.querystring.to"       = false
  }
}

resource "aws_api_gateway_integration" "latest_get" {
  http_method             = aws_api_gateway_method.latest_get.http_method
  integration_http_method = "POST"
  resource_id             = aws_api_gateway_resource.latest.id
  rest_api_id             = aws_api_gateway_rest_api.read_api.id
  type                    = "AWS_PROXY"
  uri                     = aws_lambda_function.read_api.invoke_arn

  cache_key_parameters = [
    "method.request.querystring.category",
    "method.request.querystring.from",
    "method.request.querystring.limit",
    "method.request.querystring.to"
  ]
}

resource "aws_lambda_permission" "api_gateway_read" {
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.read_api.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_api_gateway_rest_api.read_api.execution_arn}/*/${aws_api_gateway_method.latest_get.http_method}${aws_api_gateway_resource.latest.path}"
  statement_id  = "AllowApiGatewayInvoke"
}

resource "aws_api_gateway_deployment" "read_api" {
  rest_api_id = aws_api_gateway_rest_api.read_api.id

  triggers = {
    redeployment = sha1(jsonencode([
      aws_api_gateway_resource.latest.id,
      aws_api_gateway_method.latest_get.id,
      aws_api_gateway_integration.latest_get.id
    ]))
  }

  lifecycle {
    create_before_destroy = true
  }

  depends_on = [
    aws_api_gateway_integration.latest_get
  ]
}

resource "aws_api_gateway_stage" "prod" {
  cache_cluster_enabled = true
  cache_cluster_size    = "0.5"
  deployment_id         = aws_api_gateway_deployment.read_api.id
  rest_api_id           = aws_api_gateway_rest_api.read_api.id
  stage_name            = "prod"
}

resource "aws_api_gateway_method_settings" "latest_get" {
  method_path = "${trimprefix(aws_api_gateway_resource.latest.path, "/")}/${aws_api_gateway_method.latest_get.http_method}"
  rest_api_id = aws_api_gateway_rest_api.read_api.id
  stage_name  = aws_api_gateway_stage.prod.stage_name

  settings {
    cache_data_encrypted                       = true
    cache_ttl_in_seconds                       = var.read_api_cache_ttl_seconds
    caching_enabled                            = true
    metrics_enabled                            = true
    require_authorization_for_cache_control    = true
    throttling_burst_limit                     = 200
    throttling_rate_limit                      = 100
    unauthorized_cache_control_header_strategy = "FAIL_WITH_403"
  }
}

resource "aws_api_gateway_usage_plan" "external_reader" {
  name = "${var.project_name}-external-reader"

  api_stages {
    api_id = aws_api_gateway_rest_api.read_api.id
    stage  = aws_api_gateway_stage.prod.stage_name
  }

  throttle_settings {
    burst_limit = 200
    rate_limit  = 100
  }
}

resource "aws_api_gateway_api_key" "external_reader" {
  name = "${var.project_name}-external-reader"
}

resource "aws_api_gateway_usage_plan_key" "external_reader" {
  key_id        = aws_api_gateway_api_key.external_reader.id
  key_type      = "API_KEY"
  usage_plan_id = aws_api_gateway_usage_plan.external_reader.id
}
