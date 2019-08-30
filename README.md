# Azure Event Hub to Splunk Function
An Azure Function triggered by an Event Hub sending logs to Splunk.

[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fvippsas%2Fazure-event-hub-to-splunk-function%2Fwip%2FdeployAzFunction.json)

## Error handling
Logs that fail validation or being sent to Splunk will throw an exception that will be logged in Azure. To see this log, go to the Azure Function in the Azure Portal and open the Monitor tab for the function.

Logs will **not** be retried. They are gone if they fail.

## Splunk index
The Splunk index is not set in this function or its config, it is decided by the Splunk token.

## Logging format
To get logs into Splunk, they must be in a specific format. This function will make sure all logs are in that format.

To be able to convert logs to the Splunk format, the function requires all logs to contain two fields:
- `time`, used as the log `time` in Splunk.
- `source` or `app`, used as `source` in Splunk.

> For more information about logging see the [Payments Engine Logging Standard in Confluence](https://vippsas.atlassian.net/wiki/spaces/TCP/pages/965247506/Payments+Engine+and+VaaM+logging+standard).

### Example application log
```json
{
    "time": "2019-08-30T11:24:55.860400573Z",
    "app": "vipps-landing-page",
    "source": "vipps-landing-page",
    "level": "info",
    "env": "prod",
    "build": "20190830.4",
    "event": "view.index",
    "contextID": "3d54dd67-ef9d-4f43-b94f-d74eb051c607",
    "merchantSerialNumber": "559870",
    "msg": "Index view loaded."
}
```

The `time` property is parsed from the original log JSON into an Unix epoch with milliseconds.

The `source` is set from either `source` or `app` from the original log JSON. The `source` property overrides the `app` property.

### Example Splunk log
```json
{
    "time": 1567164295860.845,
    "source": "vipps-landing-page",
    "sourcetype": "_json",
    "event": {
        "time": "2019-08-30T11:24:55.860400573Z",
        "app": "vipps-landing-page",
        "source": "vipps-landing-page",
        "level": "info",
        "env": "prod",
        "build": "20190830.4",
        "event": "view.index",
        "contextID": "3d54dd67-ef9d-4f43-b94f-d74eb051c607",
        "merchantSerialNumber": "559870",
        "msg": "Index view loaded."
    }
}
```
