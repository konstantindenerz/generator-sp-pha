var Generator = require('yeoman-generator'),
    chalk = require('chalk'),
    yosay = require('yosay'),
    path = require('path'),
    vsname = require('vs_projectname'),
    uuid = require('uuid');

var generator = Generator.Base.extend({
  constructor: function(){
    Generator.Base.apply(this, arguments);

    var solutionName = vsname(this.arguments[0] || path.basename(path.resolve('.')));
    this.settings = {
      solutionName : solutionName,
      solutionNameClean: solutionName.replace('.', '').replace('_',''), // Required for SP app manifest
      solutionAppSuffix: vsname('App'),
      solutionWebSuffix: vsname('Web'),
      solutionYear: new Date().getFullYear(),
      productId: uuid(),
      solutionId: uuid().toUpperCase(),
      projectAppId:  uuid().toUpperCase(),
      projectWebId:  uuid().toUpperCase(),
      clientId: '*',
      certificatePath: '',
      certificatePassword: '',
      issuerId: '',
      globalAsaxStart: '%@',
      globalAsaxEnd: '%'
    };
  },
  info: function(){
    this.log(yosay(
      chalk.red('Welcome!') + '\n'+
      chalk.yellow('You\'re using the fantastic generator for scaffolding an ASP.NET web application with ')+
      chalk.blue('#SharePoint, #Angular ') +
      chalk.yellow(' and ') +
      chalk.blue('#Gulp') +
      chalk.yellow('. ')
    ));
  }
})

require('./src/prompts')(generator);
require('./src/project-app')(generator);
require('./src/project-web')(generator);

module.exports = generator;
